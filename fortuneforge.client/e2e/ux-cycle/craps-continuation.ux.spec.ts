import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type APIRequestContext } from '@playwright/test'

const backend = 'http://127.0.0.1:5298'
const evidence = resolve(process.cwd(), '../output/maintenance-2026-11-01/craps-continuation/browser')
const primary = (page: Page) => page.locator('.ff-craps-primary')
const close = (page: Page) => page.getByRole('button', { name: 'Close Craps details', exact: true }).click()
type Hand = { roundId: string; revision: number; rolls: unknown[]; phase: string; walletBalanceCents: number; totalWagerCents: number; totalReturnCents: number; extraBets: { kind: string; stake: number; resolved: boolean; cancelled: boolean; placedForRoll: number; resolvedOnRoll: number | null }[] }

async function open(page: Page, request: APIRequestContext, dice: number[][] = [[2, 2], [3, 6], [1, 1], [1, 3]], balanceCents = 100000) {
  const user = 'browser-' + randomUUID()
  const reset = await request.post(`${backend}/fixture/${user}/reset`, { data: { dice, balanceCents } })
  expect(reset.ok()).toBe(true)
  const writes: { path: string; body: Record<string, unknown>; status: number }[] = []
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  let lose = 0
  let balanceGate: { entered: () => void; wait: Promise<void> } | null = null
  await page.route('**/api/**', async route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (req.method() === 'GET' && path === '/api/games/craps/status' && balanceGate) {
      const held = balanceGate; balanceGate = null; held.entered(); await held.wait
    }
    const response = await route.fetch({ url: backend + path, headers: { ...req.headers(), 'X-Craps-Fixture': user } })
    if (req.method() === 'POST') writes.push({ path, body: req.postDataJSON(), status: response.status() })
    if (lose > 0 && req.method() === 'POST' && path.endsWith('/wagers')) { lose--; await route.abort('failed'); return }
    await route.fulfill({ response })
  })
  const headers = { 'X-Craps-Fixture': user }
  const api = {
    user, writes, errors,
    loseWagerResponses: (count: number) => { lose = count },
    holdBalanceRead() {
      let entered!: () => void, release!: () => void
      const entry = new Promise<void>(resolve => { entered = resolve })
      const wait = new Promise<void>(resolve => { release = resolve })
      balanceGate = { entered, wait }
      return { entered: entry, release }
    },
    hand: async (): Promise<Hand> => (await request.get(`${backend}/api/games/craps/rounds/active`, { headers })).json(),
    state: async () => (await request.get(`${backend}/fixture/state`, { headers })).json(),
    audit: async () => (await request.get(`${backend}/fixture/audit`, { headers })).json(),
    wager: async (hand: Hand, kind: string, stake: number) => request.post(`${backend}/api/games/craps/rounds/${hand.roundId}/wagers`, {
      headers, data: { kind, stake, operationId: randomUUID(), expectedRevision: hand.revision, expectedRollNumber: hand.rolls.length + 1 },
    }),
  }
  await page.goto('/games/craps')
  await expect(primary(page)).toHaveAccessibleName('Bet R10 on Pass Line')
  return api
}

async function point(page: Page) {
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll point 4')
  await expect(page.locator('.ff-craps-pass-line')).toBeDisabled()
}

async function odds(page: Page, stake: string) {
  await page.getByRole('button', { name: /^(Add|Manage|Edit|Change) odds$/, exact: false }).click()
  await page.getByRole('spinbutton', { name: 'Pass Line odds stake', exact: true }).fill(stake)
  await page.getByRole('button', { name: /^(Place|Update) odds$/, exact: false }).click()
  await expect(page.getByRole('dialog')).toContainText('Working odds R' + Number(stake).toFixed(2))
  await expect(primary(page)).toBeEnabled()
  await close(page)
}

async function side(page: Page, label: string, stake: string) {
  await page.getByRole('button', { name: 'Bets', exact: true }).click()
  const field = page.getByRole('spinbutton', { name: label + ' next-roll stake', exact: true })
  await field.fill(stake)
  await page.getByRole('button', { name: 'Save ' + label, exact: true }).click()
  await expect(field.locator('xpath=ancestor::form')).toContainText('Working R' + Number(stake).toFixed(2))
  await expect(primary(page)).toBeEnabled()
  await close(page)
}

async function screenshot(page: Page, name: string) {
  await mkdir(evidence, { recursive: true })
  await page.screenshot({ path: resolve(evidence, name + '.png'), animations: 'disabled' })
  const geometry = await page.evaluate(() => ({ width: innerWidth, height: innerHeight,
    scrollWidth: document.documentElement.scrollWidth, scrollHeight: document.documentElement.scrollHeight,
    targets: [...document.querySelectorAll('.ff-craps-primary,.ff-craps-proposition,.ff-craps-pass-line,.ff-craps-hand-actions button')].map(e => {
      const r = e.getBoundingClientRect(); return { label: e.getAttribute('aria-label') ?? e.textContent, control: e.matches('.ff-craps-primary,.ff-craps-hand-actions button'), x: r.x, y: r.y, right: r.right, bottom: r.bottom,
        hit: e.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)) }
    }) }))
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.width + 1)
  expect(geometry.scrollHeight).toBeLessThanOrEqual(geometry.height + 1)
  for (const target of geometry.targets) { expect(target.x, target.label ?? '').toBeGreaterThanOrEqual(0); expect(target.y).toBeGreaterThanOrEqual(0); expect(target.right).toBeLessThanOrEqual(geometry.width + 1); expect(target.bottom).toBeLessThanOrEqual(geometry.height + 1); expect(target.hit, target.label ?? '').toBe(true) }
  for (let left = 0; left < geometry.targets.length; left++) for (let right = left + 1; right < geometry.targets.length; right++) {
    const a = geometry.targets[left], b = geometry.targets[right]
    if (!a.control || !b.control) continue
    const overlapWidth = Math.min(a.right, b.right) - Math.max(a.x, b.x)
    const overlapHeight = Math.min(a.bottom, b.bottom) - Math.max(a.y, b.y)
    expect(overlapWidth <= 0.5 || overlapHeight <= 0.5, `${a.label} overlaps ${b.label}`).toBe(true)
  }
  return geometry
}

for (const viewport of [{ name: 'desktop', width: 1280, height: 720 }, { name: 'phone', width: 390, height: 844 }, { name: 'compact', width: 320, height: 568 }, { name: 'landscape', width: 844, height: 390 }, { name: 'short-landscape', width: 500, height: 320 }]) {
  test(`emulator wallet continuation ${viewport.name}: odds, all next-roll bets, repeated rolls, point payout and rebet`, async ({ page, request }) => {
    await page.setViewportSize(viewport)
    const api = await open(page, request)
    await point(page)
    await odds(page, '10'); await odds(page, '15'); await odds(page, '3'); await odds(page, '0'); await odds(page, '5')
    await side(page, 'Field', '4'); await side(page, 'Field', '6'); await side(page, 'Field', '2')
    await side(page, 'Any Seven', '3'); await side(page, 'Any Seven', '0'); await side(page, 'Any Seven', '1')
    await side(page, 'Any Craps', '2')
    const before = await api.hand()
    expect(before.walletBalanceCents).toBe(98000)
    expect(before.extraBets.filter(b => !b.resolved)).toHaveLength(4)
    const geometry = await screenshot(page, viewport.name + '-working')
    await primary(page).click()
    await expect(primary(page)).toHaveAccessibleName('Roll point 4')
    await expect.poll(async () => (await api.hand()).rolls.length).toBe(2)
    let hand = await api.hand()
    expect(hand.walletBalanceCents).toBe(98400)
    expect(hand.extraBets.filter(b => !b.resolved).map(b => b.kind)).toEqual(['odds'])
    await side(page, 'Field', '1'); await side(page, 'Any Craps', '1')
    await primary(page).click()
    await expect.poll(async () => (await api.hand()).rolls.length).toBe(3)
    hand = await api.hand()
    expect(hand.walletBalanceCents).toBe(99300)
    await page.reload()
    await expect(primary(page)).toHaveAccessibleName('Roll point 4')
    await primary(page).click()
    await expect(primary(page)).toHaveAccessibleName('Repeat R10')
    const state = await api.state()
    expect(state.status.walletBalanceCents).toBe(102800)
    const audit = await api.audit()
    expect(audit).toHaveLength(1)
    expect(audit[0].findings).toEqual([])
    await screenshot(page, viewport.name + '-settled')
    await primary(page).click()
    await expect(primary(page)).toHaveAccessibleName('Roll come-out')
    const repeated = await api.hand()
    expect(repeated.extraBets).toEqual([])
    expect(repeated.walletBalanceCents).toBe(101800)
    expect(api.errors).toEqual([])
    await writeFile(resolve(evidence, viewport.name + '-result.json'), JSON.stringify({ writes: api.writes, state, audit, geometry }, null, 2))
  })
}

test('emulator lost adjustment responses preserve the exact operation across reload and never debit twice', async ({ page, request }) => {
  const api = await open(page, request)
  await point(page)
  api.loseWagerResponses(2)
  await page.getByRole('button', { name: 'Add odds', exact: true }).click()
  await page.getByRole('spinbutton', { name: 'Pass Line odds stake', exact: true }).fill('7')
  await page.getByRole('button', { name: 'Place odds', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('could not be confirmed')
  await expect(primary(page)).toBeDisabled()
  const lost = api.writes.filter(w => w.path.endsWith('/wagers'))
  expect(lost).toHaveLength(2)
  expect(lost[1].body).toEqual(lost[0].body)
  expect((await api.hand()).walletBalanceCents).toBe(98300)
  await page.reload()
  await expect(primary(page)).toHaveAccessibleName('Roll point 4')
  await expect(primary(page)).toBeEnabled()
  const retried = api.writes.filter(w => w.path.endsWith('/wagers'))
  expect(retried).toHaveLength(3)
  expect(retried[2].body).toEqual(lost[0].body)
  const state = await api.state()
  expect(state.status.walletBalanceCents).toBe(98300)
  expect(state.ledgers).toHaveLength(2)
  await screenshot(page, 'lost-adjustment-recovered')
})

test('emulator stale tab refreshes, retains locked principal, rejects insufficient increase, and settles seven once', async ({ page, context, request }) => {
  const api = await open(page, request, [[2, 2], [1, 6]], 2000)
  await point(page)
  const second = await context.newPage()
  await second.route('**/api/**', async route => {
    const req = route.request()
    const response = await route.fetch({ url: backend + new URL(req.url()).pathname,
      headers: { ...req.headers(), 'X-Craps-Fixture': api.user } })
    await route.fulfill({ response })
  })
  await second.goto('/games/craps')
  await expect(primary(second)).toHaveAccessibleName('Roll point 4')
  await odds(second, '8')
  await page.getByRole('button', { name: 'Bets', exact: true }).click()
  await page.getByRole('spinbutton', { name: 'Field next-roll stake', exact: true }).fill('1')
  await page.getByRole('button', { name: 'Save Field', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText(/changed|refresh/i)
  expect(api.writes.filter(w => w.path.endsWith('/wagers'))[0].status).toBe(409)
  expect((await api.hand()).walletBalanceCents).toBe(200)
  await second.close()
  await close(page)
  const fresh = await api.hand()
  const insufficient = await api.wager(fresh, 'odds', 100)
  expect(insufficient.status()).toBe(409)
  expect((await insufficient.json()).code).toBe('craps-insufficient-credits')
  await odds(page, '0')
  await side(page, 'Any Seven', '2')
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Repeat R10')
  expect((await api.state()).status.walletBalanceCents).toBe(1800)
  expect((await api.audit())[0].findings).toEqual([])
  await page.getByRole('button', { name: 'Edit bet', exact: true }).click()
  await expect(primary(page)).toHaveAccessibleName('Bet R10 on Pass Line')
  await page.getByRole('button', { name: 'Edit amounts', exact: true }).click()
  await page.getByRole('spinbutton', { name: 'Pass Line bet in Rand', exact: true }).fill('6')
  await page.getByRole('button', { name: 'Done', exact: true }).click()
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  expect((await api.hand()).walletBalanceCents).toBe(1200)
  expect(context.pages()).toHaveLength(1)
})

test('emulator mobile touch and keyboard apply and remove a next-roll wager', async ({ browser, request }) => {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true,
    reducedMotion: 'reduce', serviceWorkers: 'block' })
  const page = await context.newPage()
  try {
    const api = await open(page, request)
    await primary(page).tap()
    await expect(primary(page)).toHaveAccessibleName('Roll come-out')
    await primary(page).tap()
    await expect(primary(page)).toHaveAccessibleName('Roll point 4')
    await page.locator('.ff-craps-proposition.is-field').tap()
    await page.getByRole('spinbutton', { name: 'Field next-roll stake', exact: true }).fill('3')
    await page.getByRole('spinbutton', { name: 'Field next-roll stake', exact: true }).press('Enter')
    await expect(page.getByRole('dialog')).toContainText('Working R3.00')
    await expect(primary(page)).toBeEnabled()
    const balance = api.holdBalanceRead()
    try {
      await page.getByRole('button', { name: 'Remove working Field', exact: true }).tap()
      await balance.entered
      await expect(page.getByRole('dialog')).toContainText('Working R0.00')
      await page.keyboard.press('Escape')
      await expect(page.getByRole('dialog')).toHaveCount(0)
      await expect(page.locator('.ff-craps-proposition.is-field')).toBeDisabled()
    } finally { balance.release() }
    await expect(page.locator('.ff-craps-proposition.is-field')).toBeFocused()
    expect((await api.hand()).walletBalanceCents).toBe(99000)
    await screenshot(page, 'touch-point-after-refund')
  } finally { await context.close() }
})

test('emulator invalid or unaffordable adjustments never write and a pending double submit debits once', async ({ page, request }) => {
  const api = await open(page, request, undefined, 1500)
  await point(page)
  await page.getByRole('button', { name: 'Bets', exact: true }).click()
  const input = page.getByRole('spinbutton', { name: 'Field next-roll stake', exact: true })
  const save = page.getByRole('button', { name: 'Save Field', exact: true })
  for (const value of ['', '0', '-1', '0.5', '101', '6']) {
    await input.fill(value)
    await expect(save).toBeDisabled()
  }
  await expect(page.getByRole('dialog')).toContainText('Insufficient balance for this increase.')
  expect(api.writes.filter(w => w.path.endsWith('/wagers'))).toHaveLength(0)
  await input.fill('3')
  await save.evaluate(button => { (button as HTMLButtonElement).click(); (button as HTMLButtonElement).click() })
  await expect(input.locator('xpath=ancestor::form')).toContainText('Working R3.00')
  await expect(save).toBeDisabled()
  expect(api.writes.filter(w => w.path.endsWith('/wagers'))).toHaveLength(1)
  expect((await api.hand()).walletBalanceCents).toBe(200)
  await page.getByRole('button', { name: 'Remove working Field', exact: true }).click()
  await expect(input.locator('xpath=ancestor::form')).toContainText('Working R0.00')
  expect((await api.hand()).walletBalanceCents).toBe(500)
})

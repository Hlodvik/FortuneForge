import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { expect, test, type Page, type TestInfo } from '@playwright/test'
import type { CrapsExtraBet, CrapsExtraBetRequest, CrapsRollResult, CrapsRound } from '../../../games/Craps/client/FortuneForge.Games.Craps.Client/src/contracts'

// The real authenticated host and packaged table render here. Every API request,
// including every wager, terminates in this in-memory fixture; no wallet is used.
const artifactDirectory = resolve(process.cwd(), '../output/maintenance-2026-11-01/craps-continuation/mocked-browser')
const viewports = [
  { name: 'desktop', width: 1280, height: 720 },
  { name: 'phone', width: 390, height: 844 },
  { name: 'compact', width: 320, height: 568 },
  { name: 'landscape', width: 844, height: 390 },
  { name: 'narrow-landscape', width: 500, height: 320 },
] as const
const primary = (page: Page) => page.locator('.ff-craps-primary')
const cell = (page: Page, kind: 'pass' | CrapsExtraBetRequest['kind']) =>
  page.locator(kind === 'pass' ? '.ff-craps-pass-line' : '.ff-craps-proposition.is-' + kind)
const fixtures = new WeakMap<Page, CrapsFixture>()

test.afterEach(async ({ page }, info) => recordFixture(page, info))

async function recordFixture(page: Page, info: TestInfo) {
  const fixture = fixtures.get(page)
  if (!fixture) return
  await mkdir(artifactDirectory, { recursive: true })
  const log = resolve(artifactDirectory, slug(info.title) + '.json')
  await writeFile(log, JSON.stringify({
    title: info.title, status: info.status, expectedStatus: info.expectedStatus,
    viewport: page.viewportSize(), writes: fixture.writes, reads: fixture.reads,
    accepted: fixture.accepted, walletBalanceCents: fixture.walletCents,
    round: fixture.round, geometry: fixture.geometry, unhandledApi: fixture.unhandledApi,
    protocolErrors: fixture.protocolErrors, pageErrors: fixture.pageErrors,
  }, null, 2))
  await info.attach('mocked-api-log', { path: log, contentType: 'application/json' })
  expect(fixture.unhandledApi, 'every API request stays inside the explicit fixture').toEqual([])
  expect(fixture.protocolErrors, 'operation IDs and replay payloads remain valid').toEqual([])
  expect(fixture.pageErrors).toEqual([])
}

for (const viewport of viewports) {
  test(`Craps betting ${viewport.name}: cells, primary action and errors fit without scrolling`, async ({ page }, info) => {
    await page.setViewportSize(viewport)
    const fixture = await openTable(page)
    await expect(primary(page)).toHaveAccessibleName('Bet R10 on Pass Line')
    await expect(page.locator('.ff-craps-guidance')).toContainText('Choose a chip')
    await fitsTable(page)
    await capture(page, info, viewport.name + '-idle')

    await cell(page, 'field').click()
    await expect(primary(page)).toHaveAccessibleName('Bet R15 total, including Pass Line R10')
    expect(fixture.writes).toHaveLength(0)
    await editAmounts(page)
    await expect(page.getByRole('spinbutton', { name: 'Pass Line bet in Rand', exact: true })).toHaveValue('10')
    await fitsDialog(page)
    await capture(page, info, viewport.name + '-exact-amounts')
    await page.getByRole('button', { name: 'Done', exact: true }).click()
    await fitsTable(page)

    await primary(page).click()
    await expect(primary(page)).toHaveAccessibleName('Roll come-out')
    await expect(page.locator('.ff-craps-guidance')).toContainText('Bets are locked')
    await fitsTable(page)
    await primary(page).click()
    await expect(primary(page)).toHaveAccessibleName('Roll point 4')
    await fitsTable(page)
    await capture(page, info, viewport.name + '-working-point')

    fixture.rejectNext = { action: 'roll', code: 'craps-roll-rejected', message: 'Fixture roll rejected. Your hand is unchanged.', status: 422 }
    await primary(page).click()
    await expect(page.getByRole('alert')).toContainText('Fixture roll rejected')
    await expect(primary(page)).toBeEnabled()
    await fitsTable(page)
    await capture(page, info, viewport.name + '-working-error')
    expect(fixture.accepted).toEqual({ start: 1, roll: 1, odds: 0 })
  })
}

test('Craps betting chip selection changes only subsequent taps and repeated taps build stacks', async ({ page }, info) => {
  const fixture = await openTable(page)
  await expect(page.getByRole('button', { name: 'Chip options, selected R5', exact: true })).toBeVisible()
  await selectChip(page, 2)
  await expect(primary(page)).toHaveAccessibleName('Bet R10 on Pass Line')
  await cell(page, 'pass').click()
  await cell(page, 'pass').click()
  await cell(page, 'field').click()
  await cell(page, 'field').click()
  await expect(cell(page, 'pass').locator('.ff-craps-chip-stack')).toHaveAttribute('aria-label', 'R14.00 on Pass Line')
  await expect(cell(page, 'field').locator('.ff-craps-chip-stack')).toHaveAttribute('aria-label', 'R4.00 on Field')
  await selectChip(page, 10)
  await expect(primary(page)).toHaveAccessibleName('Bet R18 total, including Pass Line R14')
  await cell(page, 'field').click()
  await expect(cell(page, 'field').locator('.ff-craps-chip-stack')).toHaveAttribute('aria-label', 'R14.00 on Field')
  await page.getByRole('button', { name: 'Undo last chip', exact: true }).click()
  await expect(primary(page)).toHaveAccessibleName('Bet R18 total, including Pass Line R14')
  expect(fixture.writes).toHaveLength(0)
  await capture(page, info, 'chip-selection-and-stacks')
})

test('Craps betting exact amounts, independent side edits, removal, undo and clear preserve the outbound draft', async ({ page }, info) => {
  const fixture = await openTable(page)
  for (const kind of ['field', 'any-seven', 'any-craps'] as const) await cell(page, kind).click()
  await editAmounts(page)
  await page.getByRole('spinbutton', { name: 'Pass Line bet in Rand', exact: true }).fill('6')
  await page.getByRole('spinbutton', { name: 'Field bet in Rand', exact: true }).fill('7')
  await page.getByRole('spinbutton', { name: 'Any Seven bet in Rand', exact: true }).fill('8')
  await page.getByRole('spinbutton', { name: 'Any Craps bet in Rand', exact: true }).fill('5')
  await page.getByRole('button', { name: 'Remove Any Seven bet 2', exact: true }).click()
  await expect(page.getByRole('spinbutton', { name: 'Field bet in Rand', exact: true })).toHaveValue('7')
  await expect(page.getByRole('spinbutton', { name: 'Any Craps bet in Rand', exact: true })).toHaveValue('5')
  await expect(page.getByRole('spinbutton', { name: 'Any Seven bet in Rand', exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Done', exact: true }).click()
  await page.getByRole('button', { name: 'Undo last chip', exact: true }).click()
  await expect(primary(page)).toHaveAccessibleName('Bet R26 total, including Pass Line R6')
  await page.getByRole('button', { name: 'Clear bets', exact: true }).click()
  await expect(primary(page)).toBeDisabled()
  await expect(page.locator('.ff-craps-chip-stack')).toHaveCount(0)
  await page.getByRole('button', { name: 'Undo last chip', exact: true }).click()
  await expect(primary(page)).toHaveAccessibleName('Bet R26 total, including Pass Line R6')
  expect(fixture.writes).toHaveLength(0)
  await capture(page, info, 'edited-draft-ready')
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  expect(fixture.writes).toHaveLength(1)
  expect(fixture.writes[0].body).toEqual({
    stake: 6, extraBets: [{ kind: 'field', stake: 7 }, { kind: 'any-seven', stake: 8 }, { kind: 'any-craps', stake: 5 }],
    operationId: expect.stringMatching(uuidPattern),
  })
})

test('Craps betting invalid and insufficient drafts stay editable and never write', async ({ page }, info) => {
  const fixture = await openTable(page, 2000)
  await cell(page, 'field').click()
  await editAmounts(page)
  const pass = page.getByRole('spinbutton', { name: 'Pass Line bet in Rand', exact: true })
  const field = page.getByRole('spinbutton', { name: 'Field bet in Rand', exact: true })
  for (const value of ['', '0', '-1', '0.5', '1.5', '101']) {
    await pass.fill(value)
    await expect(pass).toHaveAttribute('aria-invalid', 'true')
    await expect(primary(page)).toBeDisabled()
    expect(fixture.writes).toHaveLength(0)
  }
  await pass.fill('6')
  for (const value of ['', '0', '-1', '1.5', '101']) {
    await field.fill(value)
    await expect(field).toHaveAttribute('aria-invalid', 'true')
    await expect(primary(page)).toBeDisabled()
    expect(fixture.writes).toHaveLength(0)
  }
  await field.fill('15')
  await page.getByRole('button', { name: 'Done', exact: true }).click()
  await expect(primary(page)).toBeDisabled()
  await expect(page.locator('.ff-craps-notice')).toContainText('Insufficient balance')
  await expect(page.getByRole('alert')).toHaveAttribute('id', 'ff-craps-notice')
  await expect(page.getByRole('alert')).toContainText('Insufficient balance')
  await editAmounts(page)
  await page.getByRole('button', { name: 'Remove Field bet 1', exact: true }).click()
  await page.getByRole('button', { name: 'Done', exact: true }).click()
  await expect(primary(page)).toBeEnabled()
  await expect(primary(page)).toHaveAccessibleName('Bet R6 on Pass Line')
  await expect(page.locator('#ff-craps-notice')).not.toHaveAttribute('role', 'alert')
  expect(fixture.writes).toHaveLength(0)
  await capture(page, info, 'invalid-draft-repaired')
})

test('Craps betting accepted bets lock, point odds settle, and resolved Edit bet restores only repeatable bets', async ({ page }, info) => {
  const fixture = await openTable(page)
  fixture.sequence = [[2, 2], [1, 3]]
  await cell(page, 'field').click()
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  for (const kind of ['pass', 'field', 'any-seven', 'any-craps'] as const) await expect(cell(page, kind)).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Edit amounts', exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Bets', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Hand bets' }).getByRole('spinbutton')).toHaveCount(0)
  await page.getByRole('button', { name: 'Close Craps details', exact: true }).click()
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll point 4')
  await page.getByRole('button', { name: 'Add odds', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('Pays 2:1')
  await page.getByRole('spinbutton', { name: 'Pass Line odds stake', exact: true }).fill('3')
  await page.getByRole('button', { name: 'Place odds', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('Working odds R3.00')
  await page.getByRole('button', { name: 'Close Craps details', exact: true }).click()
  await expect(cell(page, 'pass')).toContainText('ODDS R3.00')
  await expect(page.getByRole('button', { name: 'Add odds', exact: true })).toHaveCount(0)
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Repeat R15')
  await expect(page.locator('.ff-craps-summary')).toContainText('R39.00')
  await capture(page, info, 'settled-with-odds')
  await page.getByRole('button', { name: 'Edit bet', exact: true }).click()
  await expect(primary(page)).toHaveAccessibleName('Bet R15 total, including Pass Line R10')
  await editAmounts(page)
  await expect(page.getByRole('spinbutton', { name: 'Pass Line bet in Rand', exact: true })).toHaveValue('10')
  await expect(page.getByRole('spinbutton', { name: 'Field bet in Rand', exact: true })).toHaveValue('5')
  await expect(page.getByRole('spinbutton', { name: /odds/i })).toHaveCount(0)
  expect(fixture.accepted).toEqual({ start: 1, roll: 2, odds: 1 })
})

test('Craps betting wallet after debit does not invalidate an accepted working hand', async ({ page }, info) => {
  const fixture = await openTable(page, 1500)
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  await expect(primary(page)).toBeEnabled()
  expect(fixture.walletCents).toBe(500)
  await expect(page.locator('.ff-craps-summary')).toContainText('Balance R5.00')
  await expect(page.locator('.ff-craps-notice')).not.toContainText('Insufficient balance')
  await expect(page.locator('.ff-craps-notice')).not.toContainText('Choose a Pass Line bet')
  await fitsTable(page)
  await capture(page, info, 'accepted-wallet-after-debit')
})

test('Craps betting pending double clicks serialize to one start request and one debit', async ({ page }, info) => {
  const fixture = await openTable(page)
  const pending = fixture.holdNext('start')
  try {
    await primary(page).evaluate(button => { (button as HTMLButtonElement).click(); (button as HTMLButtonElement).click() })
    await pending.entered
    await expect(primary(page)).toBeDisabled()
    await expect(cell(page, 'pass')).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Chip options, selected R5', exact: true })).toBeDisabled()
    expect(fixture.writes).toHaveLength(1)
    await capture(page, info, 'start-pending')
  } finally { pending.release() }
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  expect(fixture.accepted.start).toBe(1)
  expect(fixture.walletCents).toBe(99000)
})

test('Craps betting a known rejected start preserves the exact draft without replay', async ({ page }, info) => {
  const fixture = await openTable(page)
  await cell(page, 'field').click()
  await editAmounts(page)
  await page.getByRole('spinbutton', { name: 'Pass Line bet in Rand', exact: true }).fill('6')
  await page.getByRole('button', { name: 'Done', exact: true }).click()
  fixture.rejectNext = { action: 'start', code: 'craps-stake-rejected', message: 'Fixture bet rejected. Review your amounts.', status: 422 }
  await primary(page).click()
  await expect(page.getByRole('alert')).toContainText('Fixture bet rejected')
  await expect(primary(page)).toHaveAccessibleName('Bet R11 total, including Pass Line R6')
  await expect(primary(page)).toBeEnabled()
  await expect(cell(page, 'field').locator('.ff-craps-chip-stack')).toHaveAttribute('aria-label', 'R5.00 on Field')
  expect(fixture.writes).toHaveLength(1)
  expect(fixture.accepted.start).toBe(0)
  expect(fixture.walletCents).toBe(100000)
  await capture(page, info, 'rejected-start-draft-preserved')
})

test('Craps betting 409 restores the existing hand by reads without replaying start', async ({ page }, info) => {
  const fixture = await openTable(page)
  fixture.conflictNextStart = true
  await primary(page).click()
  await expect(page.getByRole('alert')).toContainText('Your existing Craps hand was restored')
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  await expect(cell(page, 'pass')).toBeDisabled()
  await expect(cell(page, 'pass').locator('.ff-craps-chip-stack')).toHaveAttribute('aria-label', 'R12.00 on Pass Line')
  expect(fixture.writes).toHaveLength(1)
  expect(fixture.accepted).toEqual({ start: 0, roll: 0, odds: 0 })
  expect(fixture.reads.filter(path => path.endsWith('/rounds/active'))).toHaveLength(2)
  await capture(page, info, 'active-hand-conflict-restored')
})

test('Craps betting an uncertain roll retries the same operation and reload confirms its cached result once', async ({ page }, info) => {
  const fixture = await openTable(page)
  await primary(page).click()
  await expect(primary(page)).toHaveAccessibleName('Roll come-out')
  fixture.loseResponses.roll = 2
  await primary(page).click()
  await expect(page.getByRole('alert')).toContainText('could not be confirmed')
  await expect(primary(page)).toBeDisabled()
  await expect(page.locator('.ff-craps-guidance')).toContainText('Check again')
  let rolls = fixture.writes.filter(write => write.action === 'roll')
  expect(rolls).toHaveLength(2)
  expect(rolls[0].body).toEqual(rolls[1].body)
  expect(fixture.accepted.roll).toBe(1)
  await capture(page, info, 'uncertain-roll-locked')
  await page.reload()
  await expect(primary(page)).toHaveAccessibleName('Roll point 4')
  await expect(primary(page)).toBeEnabled()
  await expect(page.getByRole('alert')).toHaveCount(0)
  rolls = fixture.writes.filter(write => write.action === 'roll')
  expect(rolls).toHaveLength(3)
  expect(new Set(rolls.map(write => write.body.operationId)).size).toBe(1)
  expect(fixture.accepted).toEqual({ start: 1, roll: 1, odds: 0 })
  expect(fixture.round?.rolls).toHaveLength(1)
  expect(fixture.walletCents).toBe(99000)
  expect(await page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('fortuneforge:craps:pending:')))).toEqual([])
  await fitsTable(page)
  await capture(page, info, 'uncertain-roll-reload-confirmed')
})

test.describe('emulated touch and keyboard', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true })
  test('Craps betting emulated touch and keyboard edit, dismiss and place exactly one pending action', async ({ page }, info) => {
    const fixture = await openTable(page)
    await page.getByRole('button', { name: 'Chip options, selected R5', exact: true }).tap()
    await page.getByRole('button', { name: 'Select R2 chip', exact: true }).tap()
    await cell(page, 'pass').tap()
    await cell(page, 'field').tap()
    await expect(primary(page)).toHaveAccessibleName('Bet R14 total, including Pass Line R12')
    const editor = page.getByRole('button', { name: 'Edit amounts', exact: true })
    await editor.tap()
    await page.getByRole('spinbutton', { name: 'Pass Line bet in Rand', exact: true }).fill('6')
    await page.getByRole('spinbutton', { name: 'Field bet in Rand', exact: true }).fill('4')
    await page.getByRole('button', { name: 'Done', exact: true }).tap()
    await editor.tap()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(editor).toBeFocused()
    await fitsTable(page)
    expect(fixture.writes).toHaveLength(0)
    await primary(page).tap()
    await expect(primary(page)).toHaveAccessibleName('Roll come-out')
    expect(fixture.writes[0].body).toEqual({ stake: 6, extraBets: [{ kind: 'field', stake: 4 }], operationId: expect.stringMatching(uuidPattern) })
    const pending = fixture.holdNext('roll')
    try {
      await primary(page).focus()
      await page.keyboard.press('Enter')
      await pending.entered
      await expect(primary(page)).toBeDisabled()
      await page.keyboard.press('Enter')
      expect(fixture.writes.filter(write => write.action === 'roll')).toHaveLength(1)
    } finally { pending.release() }
    await expect(primary(page)).toHaveAccessibleName('Roll point 4')
    expect(fixture.accepted).toEqual({ start: 1, roll: 1, odds: 0 })
    await fitsTable(page)
    await capture(page, info, 'touch-and-keyboard-working')
  })
})

async function openTable(page: Page, walletCents = 100000) {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  const fixture = await mockPaidCraps(page, walletCents)
  await page.goto('/games/craps')
  await expect(primary(page)).toBeEnabled()
  await page.evaluate(() => document.fonts.ready)
  return fixture
}

async function editAmounts(page: Page) {
  await page.getByRole('button', { name: 'Edit amounts', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Hand bets', exact: true })).toBeVisible()
}

async function selectChip(page: Page, amount: number) {
  await page.getByRole('button', { name: /^Chip options, selected / }).click()
  await page.getByRole('button', { name: `Select R${amount} chip`, exact: true }).click()
  await expect(page.getByRole('button', { name: `Chip options, selected R${amount}`, exact: true })).toHaveAttribute('aria-expanded', 'false')
}

async function fitsTable(page: Page) {
  const geometry = await page.evaluate(() => {
    const bounds = (element: Element) => {
      const box = element.getBoundingClientRect()
      return { x: box.x, y: box.y, right: box.right, bottom: box.bottom, width: box.width, height: box.height }
    }
    return {
      width: innerWidth, height: innerHeight,
      document: { width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight },
      scrolls: [...document.querySelectorAll('.in-game-shell__body,.ff-craps-page,.ff-craps-main,.ff-craps-table')].map(element => ({
        className: element.className, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
        scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
        overflowX: getComputedStyle(element).overflowX, overflowY: getComputedStyle(element).overflowY,
      })),
      // .ff-craps-propositions is display:contents and has no meaningful box.
      targets: [...document.querySelectorAll('.ff-craps-proposition,.ff-craps-pass-line,.ff-craps-primary')].map(element => {
        const box = element.getBoundingClientRect()
        return { className: element.className, ...bounds(element), centerHits: element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)) }
      }),
    }
  })
  fixtures.get(page)?.geometry.push(geometry)
  expect(geometry.document.width).toBeLessThanOrEqual(geometry.width + 1)
  expect(geometry.document.height).toBeLessThanOrEqual(geometry.height + 1)
  expect(geometry.targets).toHaveLength(5)
  for (const target of geometry.targets) {
    expect(target.x, target.className).toBeGreaterThanOrEqual(-0.5)
    expect(target.y, target.className).toBeGreaterThanOrEqual(-0.5)
    expect(target.right, target.className).toBeLessThanOrEqual(geometry.width + 0.5)
    expect(target.bottom, target.className).toBeLessThanOrEqual(geometry.height + 0.5)
    expect(target.width, target.className).toBeGreaterThanOrEqual(32)
    expect(target.height, target.className).toBeGreaterThanOrEqual(32)
    expect(target.centerHits, target.className + ' center is unobstructed').toBe(true)
  }
  for (const scroll of geometry.scrolls) {
    // A visible-overflow layout box is not a scrolling container. Its children
    // are checked against the viewport above; actual game scroll/clip boxes
    // must contain their content without needing a scroll offset.
    if (['auto', 'scroll', 'hidden'].includes(scroll.overflowX)) expect(scroll.scrollWidth, scroll.className).toBeLessThanOrEqual(scroll.clientWidth + 1)
    if (['auto', 'scroll', 'hidden'].includes(scroll.overflowY)) expect(scroll.scrollHeight, scroll.className).toBeLessThanOrEqual(scroll.clientHeight + 1)
  }
}

async function fitsDialog(page: Page) {
  const box = await page.getByRole('dialog').boundingBox()
  const viewport = page.viewportSize()!
  expect(box).not.toBeNull()
  expect(box!.x).toBeGreaterThanOrEqual(0)
  expect(box!.y).toBeGreaterThanOrEqual(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width + 0.5)
  expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height + 0.5)
  // Only the dialog body may scroll; Playwright may scroll it to reach Done.
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Done', exact: true })).toBeVisible()
}

async function capture(page: Page, info: TestInfo, name: string) {
  await mkdir(artifactDirectory, { recursive: true })
  const path = resolve(artifactDirectory, name + '.png')
  await page.screenshot({ path, animations: 'disabled' })
  await info.attach(name, { path, contentType: 'image/png' })
}

type Action = 'start' | 'roll' | 'odds'
type WriteBody = { operationId: string; stake?: number; extraBets?: CrapsExtraBetRequest[]; kind?: string; expectedRevision?: number; expectedRollNumber?: number }
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
type CrapsFixture = Awaited<ReturnType<typeof mockPaidCraps>>

async function mockPaidCraps(page: Page, initialWallet: number) {
  let pending: { action: Action; entered: () => void; wait: Promise<void> } | null = null
  const cache = new Map<string, { request: string; response: CrapsRound }>()
  const fixture = {
    round: null as CrapsRound | null, walletCents: initialWallet,
    sequence: [[2, 2], [3, 6], [1, 3]] as [number, number][], index: 0, count: 0,
    accepted: { start: 0, roll: 0, odds: 0 },
    writes: [] as { action: Action; path: string; body: WriteBody }[], reads: [] as string[],
    unhandledApi: [] as string[], protocolErrors: [] as string[], pageErrors: [] as string[], geometry: [] as unknown[],
    rejectNext: null as { action: Action; status: number; code: string; message: string } | null,
    conflictNextStart: false,
    loseResponses: { start: 0, roll: 0, odds: 0 },
    holdNext(action: Action) {
      let release!: () => void, entered!: () => void
      const wait = new Promise<void>(resolve => { release = resolve })
      const entry = new Promise<void>(resolve => { entered = resolve })
      pending = { action, entered, wait }
      return { release, entered: entry }
    },
  }
  fixtures.set(page, fixture)
  page.on('pageerror', error => fixture.pageErrors.push(error.message))
  const account = () => ({
    userId: 'craps-betting-fixture', playerName: 'Craps test player', email: 'craps@example.test',
    createdAtUtc: '2026-01-01T00:00:00Z', role: 'player',
    balances: { slotsCredits: fixture.walletCents / 100, freeGames: 0 },
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  })
  function newRound(stake: number, extras: readonly CrapsExtraBetRequest[]): CrapsRound {
    fixture.count++; fixture.index = 0
    fixture.walletCents -= cents(stake + extras.reduce((sum, bet) => sum + bet.stake, 0))
    return { roundId: '00000000-0000-4000-8000-' + String(fixture.count).padStart(12, '0'), stake,
      phase: 'come-out', point: null, rolls: [], lastOutcome: null,
      extraBets: extras.map(bet => ({ ...bet, resolved: false, won: false, totalReturn: null })), walletBalanceCents: fixture.walletCents,
      revision: 0, totalWagerCents: cents(stake + extras.reduce((sum, bet) => sum + bet.stake, 0)), totalReturnCents: 0 }
  }
  await page.route('**/api/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname, method = request.method()
    const json = (value: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    if (method === 'GET') fixture.reads.push(path)
    if (path === '/api/accounts/me' && method === 'GET') return json(account())
    if (path === '/api/games/craps/status' && method === 'GET') return json({
      available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1,
      mode: 'paid-pass-line', walletBalanceCents: fixture.walletCents,
    })
    if (path === '/api/games/craps/rounds/active' && method === 'GET') {
      return fixture.round && fixture.round.phase !== 'resolved' ? json(fixture.round) : route.fulfill({ status: 204 })
    }
    if (/^\/api\/games\/craps\/rounds\/[^/]+$/.test(path) && method === 'GET') {
      return fixture.round && path.endsWith('/' + fixture.round.roundId) ? json(fixture.round)
        : json({ code: 'craps-round-not-found', message: 'Fixture hand expired.' }, 404)
    }
    const action: Action | null = method !== 'POST' ? null : path === '/api/games/craps/rounds' ? 'start'
      : /^\/api\/games\/craps\/rounds\/[^/]+\/roll$/.test(path) ? 'roll'
      : /^\/api\/games\/craps\/rounds\/[^/]+\/(odds|wagers)$/.test(path) ? 'odds' : null
    if (!action) {
      fixture.unhandledApi.push(method + ' ' + path)
      return json({ code: 'fixture-unhandled-api', message: 'No API request leaves this test fixture.' }, 501)
    }
    const body = request.postDataJSON() as WriteBody
    fixture.writes.push({ action, path, body })
    if (!uuidPattern.test(body.operationId ?? '')) {
      fixture.protocolErrors.push('Missing UUID operationId: ' + path)
      return json({ code: 'fixture-invalid-operation', message: 'A UUID operationId is required.' }, 400)
    }
    if (pending?.action === action) { const held = pending; pending = null; held.entered(); await held.wait }
    if (fixture.rejectNext?.action === action) {
      const rejection = fixture.rejectNext; fixture.rejectNext = null
      return json({ code: rejection.code, message: rejection.message }, rejection.status)
    }
    if (action === 'start' && fixture.conflictNextStart) {
      fixture.conflictNextStart = false
      fixture.round = newRound(12, [{ kind: 'field', stake: 3 }])
      return json({ code: 'craps-round-active', message: 'An existing fixture hand is active.' }, 409)
    }
    const key = path + ':' + body.operationId, previous = cache.get(key)
    if (previous && previous.request !== JSON.stringify(body)) {
      fixture.protocolErrors.push('Operation reused with changed payload: ' + key)
      return json({ code: 'craps-operation-conflict', message: 'Operation payload changed.' }, 409)
    }
    let response = previous?.response
    if (!response) {
      if (action === 'start') fixture.round = newRound(body.stake!, body.extraBets ?? [])
      else {
        const hand = fixture.round
        if (!hand || !path.includes('/' + hand.roundId + '/')) return json({ code: 'craps-round-not-found', message: 'Fixture hand missing.' }, 404)
        if (body.expectedRevision !== hand.revision || body.expectedRollNumber !== hand.rolls.length + 1) {
          fixture.protocolErrors.push('Missing or stale action context: ' + path)
          return json({ code: 'craps-stale-revision', message: 'Refresh the fixture hand.' }, 409)
        }
        if (action === 'odds') {
          if (path.endsWith('/wagers') && body.kind !== 'odds') {
            fixture.protocolErrors.push('This pre-bet fixture only expects odds adjustments: ' + path)
            return json({ code: 'fixture-unexpected-kind', message: 'Use the real emulator continuation fixture for side adjustments.' }, 400)
          }
          fixture.walletCents -= cents(body.stake!)
          fixture.round = { ...hand, extraBets: [...(hand.extraBets ?? []), { kind: 'odds', stake: body.stake!, resolved: false, won: false, totalReturn: null }], walletBalanceCents: fixture.walletCents,
            revision: hand.revision! + 1, totalWagerCents: hand.totalWagerCents! + cents(body.stake!) }
        } else {
          // Deterministic display fixtures, not an independent test of game math.
          const [first, second] = fixture.sequence[fixture.index++] ?? [1, 6], total = first + second
          const result: CrapsRollResult = hand.phase === 'come-out'
            ? [7, 11].includes(total) ? 'natural-win' : [2, 3, 12].includes(total) ? 'craps-loss' : 'point-established'
            : total === hand.point ? 'point-hit' : total === 7 ? 'seven-out' : 'no-decision'
          const terminal = ['natural-win', 'craps-loss', 'point-hit', 'seven-out'].includes(result)
          const won = result === 'natural-win' || result === 'point-hit'
          const point = result === 'point-established' ? total : hand.point
          let credit = terminal && won ? hand.stake * 2 : 0
          const extras: CrapsExtraBet[] = (hand.extraBets ?? []).map(bet => {
            if (bet.resolved || bet.kind === 'odds' && !terminal) return bet
            const success = bet.kind === 'odds' ? won : bet.kind === 'field' ? [2, 3, 4, 9, 10, 11, 12].includes(total) : bet.kind === 'any-seven' ? total === 7 : [2, 3, 12].includes(total)
            const factor = bet.kind === 'odds' ? point === 4 || point === 10 ? 3 : point === 5 || point === 9 ? 2.5 : 2.2
              : bet.kind === 'field' ? [2, 12].includes(total) ? 3 : 2 : bet.kind === 'any-seven' ? 5 : 8
            const totalReturn = success ? cents(bet.stake * factor) / 100 : 0
            credit += totalReturn
            return { ...bet, resolved: true, won: success, totalReturn }
          })
          fixture.walletCents += cents(credit)
          fixture.round = { ...hand, phase: terminal ? 'resolved' : 'point', point,
            rolls: [...hand.rolls.map(roll => ({ ...roll, result: null })), { rollNumber: hand.rolls.length + 1, first, second, total, result }],
            lastOutcome: { first, second, total, result, isTerminal: terminal, totalReturn: terminal ? won ? hand.stake * 2 : 0 : null },
            extraBets: extras, walletBalanceCents: fixture.walletCents, revision: hand.revision! + 1,
            totalReturnCents: hand.totalReturnCents! + cents(credit) }
        }
      }
      fixture.accepted[action]++
      response = structuredClone(fixture.round!)
      cache.set(key, { request: JSON.stringify(body), response })
    }
    if (fixture.loseResponses[action] > 0) { fixture.loseResponses[action]--; return route.abort('failed') }
    return json(response)
  })
  return fixture
}

function cents(value: number) { return Math.round(value * 100) }
function slug(value: string) { return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') }

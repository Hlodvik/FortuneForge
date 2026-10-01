import { expect, test, type Page, type TestInfo } from '@playwright/test'
import type { AccountSummary } from '../../src/features/account/services/accountsApi'
import type { BlackjackTableHand, BlackjackTablePlaySession, BlackjackTableSeat, BlackjackTableStatus } from '../../src/games/cards/blackjack/blackjackTableApi'

const account: AccountSummary = {
  userId: 'blackjack-ux-player', playerName: 'Alex', email: 'blackjack-ux@example.test', createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 25, freeGames: 0 }, role: 'Player',
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
const status: BlackjackTableStatus = {
  available: true, minimumWager: .5, maximumWager: 100, wagerIncrement: .5, minimumStartOccupancy: 3, tableCapacity: 5,
  humanGraceSeconds: 5, actionDeadlineSeconds: 60, deckCount: 6, surrenderAllowed: true,
  dealerRule: 'Stands on 17', blackjackPayout: '3:2', doubleAllowed: true, splitAllowed: true, insuranceAllowed: true,
}
const viewports = [
  { name: 'desktop', width: 1280, height: 720 },
  { name: 'phone', width: 390, height: 844 },
  { name: 'compact-phone', width: 320, height: 568 },
  { name: 'landscape', width: 667, height: 375 },
  { name: 'wide-landscape', width: 852, height: 393 },
] as const

for (const viewport of viewports) {
  test(`Blackjack ${viewport.name} keeps betting, dense hands, split hands and settlement in view`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    const fixture = await mockTable(page)
    await page.goto('/cards/blackjack')
    const wager = page.getByRole('spinbutton', { name: 'Wager amount' })
    await expect(wager).toBeEnabled()
    expect(await page.evaluate(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(true)
    await page.evaluate(() => document.fonts.ready)
    await expectTableFits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-betting`)
    const playfield = await page.locator('.blackjack-playfield').boundingBox()

    await wager.fill('')
    await expect(wager).toHaveValue('')
    await expect(page.getByRole('button', { name: 'Wager', exact: true })).toBeDisabled()
    await page.getByRole('button', { name: 'Increase wager' }).click()
    await expect(wager).toHaveValue('0.5')
    await wager.fill('5.2')
    await expect(wager).toHaveAttribute('aria-invalid', 'true')
    await page.getByRole('button', { name: 'Increase wager' }).click()
    await expect(wager).toHaveValue('5.5')
    await wager.fill('26')
    await expect(page.getByRole('button', { name: 'Wager', exact: true })).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Increase wager' })).toBeDisabled()
    await wager.fill('5')
    await wager.press('Enter')
    await expect(page.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled()
    expect(fixture.requests[0].body).toEqual({ wager: 5, expectedVersion: 1 })
    await expectTableFits(page, viewport)
    expect(await page.locator('.blackjack-playfield').boundingBox()).toEqual(playfield)
    await capture(page, testInfo, `${viewport.name}-active`)

    await page.getByRole('button', { name: 'Hit', exact: true }).click()
    const human = page.locator('.blackjack-seat.is-current')
    await expect(human.getByLabel('Hand total 17', { exact: true })).toBeVisible()
    await expectTableFits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-dense-hand`)
    await page.getByRole('button', { name: 'Split', exact: true }).click()
    await expect(human.getByRole('heading', { name: 'Hand 2' })).toBeVisible()
    await expect(human.locator('.blackjack-seat__hands > .is-active-hand')).toHaveCount(1)
    await expectTableFits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-split`)

    await page.getByRole('button', { name: 'Stand', exact: true }).click()
    await expect(page.locator('.blackjack-payout-flight')).toHaveText('+R10.00')
    await expect(page.locator('.blackjack-payout-flight')).toHaveCSS('animation-name', 'none')
    await expect(page.locator('.blackjack-balance-bubble strong')).toHaveText('R30.00')
    await expect(page.getByRole('status')).toHaveText(/Next round \d+/)
    await expectTableFits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-settlement`)

    const help = page.getByRole('button', { name: 'How to play Blackjack' })
    await help.click()
    await expect(page.getByRole('button', { name: 'Close how to play' })).toBeFocused()
    const panel = page.getByRole('dialog', { name: 'How to play Blackjack' })
    const bounds = await panel.boundingBox()
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height + 1)
    await capture(page, testInfo, `${viewport.name}-help`)
    await page.keyboard.press('Escape')
    await expect(panel).toHaveCount(0)
    await expect(help).toBeFocused()
    await help.click()
    await page.getByRole('button', { name: 'Close how to play' }).click()
    await expect(help).toBeFocused()
    await page.getByRole('button', { name: 'Leave table', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Join live table' })).toBeVisible()
  })
}

test('Blackjack preserves pending wager and action keys while preventing competing requests', async ({ page }, testInfo) => {
  const fixture = await mockTable(page, { failWager: true, failHit: true })
  await page.goto('/cards/blackjack')
  const wager = page.getByRole('spinbutton', { name: 'Wager amount' })
  await expect(wager).toBeEnabled()
  await wager.fill('5')
  await wager.focus()
  expect(await wager.evaluate(element => getComputedStyle(element).outlineStyle)).toBe('solid')
  await page.getByRole('button', { name: 'Wager', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Connection interrupted')
  await expect(wager).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Sit out', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Leave table', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Wager', exact: true })).toBeEnabled()
  await capture(page, testInfo, 'pending-wager')
  await page.getByRole('button', { name: 'Wager', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled()
  expect(fixture.requests[1]).toEqual(fixture.requests[0])
  await page.getByRole('button', { name: 'Hit', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Connection interrupted')
  await expect(page.getByRole('button', { name: 'Stand', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Split', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Leave table', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Hit', exact: true })).toBeEnabled()
  await capture(page, testInfo, 'pending-hit')
  await page.getByRole('button', { name: 'Hit', exact: true }).click()
  await expect(page.locator('.blackjack-seat.is-current').getByLabel('Hand total 17', { exact: true })).toBeVisible()
  expect(fixture.requests[3]).toEqual(fixture.requests[2])
  await page.getByRole('button', { name: 'Stand', exact: true }).click()
  expect(fixture.requests[4].key).not.toBe(fixture.requests[3].key)
})

test('Blackjack sit out keeps the player seat and submits no wager', async ({ page }) => {
  const fixture = await mockTable(page)
  await page.goto('/cards/blackjack')
  await page.getByRole('button', { name: 'Sit out', exact: true }).click()
  await expect(page.locator('.blackjack-seat.is-current')).toContainText('Alex')
  await expect(page.getByRole('button', { name: 'Wager', exact: true })).toHaveCount(0)
  expect(fixture.requests).toHaveLength(1)
  expect(fixture.requests[0].path).toMatch(/\/sit-out$/)
  expect(fixture.requests[0].body).toEqual({ expectedVersion: 1 })
  await expect(page.locator('.blackjack-balance-bubble strong')).toHaveText('R25.00')
})

test('Blackjack keeps win flight for normal motion and responds to a changed motion preference', async ({ page }, testInfo) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await mockTable(page)
  await page.goto('/cards/blackjack')
  await page.getByRole('button', { name: 'Wager', exact: true }).click()
  await page.getByRole('button', { name: 'Stand', exact: true }).click()
  const win = page.locator('.blackjack-payout-flight')
  await expect(win).toHaveText('+R10.00')
  await expect(win).toHaveCSS('animation-name', 'blackjack-payout-flight')
  await expect(win).toHaveAttribute('style', /--blackjack-flight-x/)
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(win).toHaveClass(/is-still/)
  await expect(win).toHaveCSS('animation-name', 'none')
  await expect(page.locator('.blackjack-balance-bubble strong')).toHaveText('R30.00')
  await capture(page, testInfo, 'motion-preference-changed')
})

async function expectTableFits(page: Page, viewport: { width: number; height: number }) {
  expect(await page.evaluate(() => Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))).toBeLessThanOrEqual(viewport.height + 1)
  const surfaces = await page.locator('.blackjack-table, .blackjack-dealer, .blackjack-seat, .blackjack-actions > button, .blackjack-wager > button, .blackjack-leave').all()
  for (const surface of surfaces) {
    const box = await surface.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(-1)
    expect(box!.y).toBeGreaterThanOrEqual(-1)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width + 1)
    expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height + 1)
    if (viewport.width <= 852 && await surface.evaluate(element => element.tagName === 'BUTTON')) expect(box!.height).toBeGreaterThanOrEqual(44)
  }
  const seats = await page.locator('.blackjack-seat').evaluateAll(elements => elements.map(e => e.getBoundingClientRect().toJSON()))
  for (let i = 0; i < seats.length; i++) for (let j = i + 1; j < seats.length; j++) {
    const a = seats[i], b = seats[j]
    expect(a.right <= b.left + 1 || b.right <= a.left + 1 || a.bottom <= b.top + 1 || b.bottom <= a.top + 1).toBe(true)
  }
  for (const seat of await page.locator('.blackjack-seat').all()) {
    const bounds = await seat.boundingBox()
    for (const card of await seat.locator('.ff-card-slot').all()) {
      const box = await card.boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(bounds!.x - 1)
      expect(box!.x + box!.width).toBeLessThanOrEqual(bounds!.x + bounds!.width + 1)
    }
  }
}

async function capture(page: Page, testInfo: TestInfo, name: string) {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, animations: 'disabled', caret: 'hide' })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

function hand(ranks: string[], score: number): BlackjackTableHand {
  return { cards: ranks.map((rank, i) => ({ rank, suit: i % 2 ? 'spades' : 'hearts', hidden: false })), score, soft: false, blackjack: false, bust: false }
}
function initialSession(): BlackjackTablePlaySession {
  const seats: BlackjackTableSeat[] = ['Alex', 'Mina', 'Leo', 'Nora', 'Sam'].map((displayName, seat) => ({
    seatId: `ux-seat-${seat}`, displayName, seat, status: seat === 0 ? 'awaiting-wager' : 'stood',
    wager: 5, totalWager: 5, payout: 0, outcome: null, lastAction: null, isCurrentPlayer: seat === 0,
    hand: seat === 1 ? hand(['2', '3', '2', '4', '3', '4'], 18) : hand(['8', '3'], 11),
  }))
  return {
    contractVersion: 'cards.blackjack.table.v2', kind: 'table', version: 1,
    table: {
      tableId: 'ux-table', phase: 'betting', round: 1, seats, activeSeat: 0, legalActions: [],
      dealer: { ...hand(['K'], 10), cards: [{ rank: 'K', suit: 'diamonds', hidden: false }, { rank: null, suit: null, hidden: true }] },
      createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
      actionDeadlineAtUtc: null, wagerDeadlineAtUtc: new Date(Date.now() + 60_000).toISOString(), transition: 'human-wager',
      nextTransitionAtUtc: null, remainingActionMilliseconds: 0, remainingWagerMilliseconds: 60_000, remainingTransitionMilliseconds: 0,
    },
  }
}
async function mockTable(page: Page, options: { failWager?: boolean; failHit?: boolean } = {}) {
  let session = initialSession()
  let balance = 25
  let left = false
  let failedWager = false
  let failedHit = false
  const requests: { path: string; body: Record<string, unknown>; key: string | undefined }[] = []
  await page.addInitScript(() => Object.defineProperty(window, 'AudioContext', { configurable: true, value: undefined }))
  await page.route('**/api/**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path === '/api/accounts/me') return route.fulfill({ json: { ...account, balances: { ...account.balances, slotsCredits: balance } } })
    if (path.endsWith('/status')) return route.fulfill({ json: status })
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>
      requests.push({ path, body, key: request.headers()['idempotency-key'] })
      if (options.failWager && !failedWager && path.endsWith('/wagers')) {
        failedWager = true
        return route.fulfill({ status: 503, json: { code: 'blackjack-table-request-failed', error: 'Connection interrupted' } })
      }
      if (options.failHit && !failedHit && body.type === 'hit') {
        failedHit = true
        return route.fulfill({ status: 503, json: { code: 'blackjack-table-request-failed', error: 'Connection interrupted' } })
      }
      session = structuredClone(session)
      session.version++
      const table = session.table
      const current = { ...table.seats[0] }
      table.seats = [current, ...table.seats.slice(1)]
      if (path.endsWith('/wagers')) {
        balance -= Number(body.wager)
        current.status = 'playing'
        table.phase = 'active'
        table.transition = null
        table.actionDeadlineAtUtc = new Date(Date.now() + 60_000).toISOString()
        table.legalActions = ['hit', 'stand', 'double', 'split', 'surrender']
      } else if (body.type === 'hit') {
        current.hand = hand(['2', '3', '2', '4', '3', '3'], 17)
      } else if (body.type === 'split') {
        current.hands = [1, 2].map(handNumber => ({ handNumber, hand: handNumber === 1 ? hand(['2', '3', '2', '4', '3', '3'], 17) : hand(['8', '8'], 16), wager: 5, totalWager: 5, payout: 0, status: 'playing', outcome: null, lastAction: null, active: handNumber === 2 }))
      } else if (body.type === 'stand') {
        balance += 10
        current.payout = 10
        current.outcome = 'player-win'
        current.status = 'completed'
        table.phase = 'settlement'
        table.transition = 'next-round-countdown'
        table.nextTransitionAtUtc = new Date(Date.now() + 10_000).toISOString()
        table.legalActions = []
        table.activeSeat = null
      } else if (path.endsWith('/sit-out')) {
        current.status = 'sitting-out'
        table.activeSeat = 1
        table.transition = 'wager-settle'
      } else if (path.endsWith('/leave')) left = true
    }
    return route.fulfill({ json: { session: left ? { contractVersion: 'cards.blackjack.table.v2', kind: 'idle', version: session.version } : session, balanceCredits: balance } })
  })
  return { requests }
}

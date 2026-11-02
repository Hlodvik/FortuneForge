import { expect, test, type Page, type TestInfo, type Locator } from '@playwright/test'
import type { BaccaratBetSide, BaccaratCard, BaccaratRound, BaccaratStatus } from '../../../games/Baccarat/client/FortuneForge.Games.Baccarat.Client/src/contracts'
import type { AccountSummary } from '../../src/features/account/services/accountsApi'

const account: AccountSummary = {
  userId: 'baccarat-ux-player', playerName: 'Alex', email: 'baccarat-ux@example.test', createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 1_000, freeGames: 0 }, role: 'Player',
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
const status: BaccaratStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, balance: 1_000, mode: 'test' }
const viewports = [
  { name: 'desktop', width: 1280, height: 720 },
  { name: 'phone', width: 390, height: 844 },
  { name: 'short-phone', width: 390, height: 700 },
  { name: 'compact-phone', width: 320, height: 568 },
  { name: 'landscape', width: 667, height: 375 },
  { name: 'wide-landscape', width: 852, height: 393 },
  { name: 'short-desktop', width: 1280, height: 480 },
] as const
type HandKind = 'natural-banker' | 'banker-third' | 'player-third-tie' | 'both-third' | 'natural-player' | 'natural-tie'
const card = (rank: BaccaratCard['rank'], suit: BaccaratCard['suit']): BaccaratCard => ({ rank, suit })
const hands = {
  'natural-banker': { playerCards: [card('eight', 'spades'), card('eight', 'spades')], bankerCards: [card('nine', 'clubs'), card('king', 'diamonds')], playerTotal: 6, bankerTotal: 9, outcome: 'banker', endedOnNatural: true },
  'banker-third': { playerCards: [card('three', 'clubs'), card('three', 'spades')], bankerCards: [card('two', 'clubs'), card('three', 'hearts'), card('two', 'hearts')], playerTotal: 6, bankerTotal: 7, outcome: 'banker', endedOnNatural: false },
  'player-third-tie': { playerCards: [card('ace', 'clubs'), card('two', 'diamonds'), card('four', 'spades')], bankerCards: [card('two', 'clubs'), card('five', 'hearts')], playerTotal: 7, bankerTotal: 7, outcome: 'tie', endedOnNatural: false },
  'both-third': { playerCards: [card('ace', 'clubs'), card('two', 'diamonds'), card('six', 'spades')], bankerCards: [card('two', 'clubs'), card('two', 'hearts'), card('three', 'diamonds')], playerTotal: 9, bankerTotal: 7, outcome: 'player', endedOnNatural: false },
  'natural-player': { playerCards: [card('eight', 'clubs'), card('king', 'hearts')], bankerCards: [card('three', 'spades'), card('ace', 'clubs')], playerTotal: 8, bankerTotal: 4, outcome: 'player', endedOnNatural: true },
  'natural-tie': { playerCards: [card('four', 'clubs'), card('four', 'diamonds')], bankerCards: [card('eight', 'hearts'), card('king', 'hearts')], playerTotal: 8, bankerTotal: 8, outcome: 'tie', endedOnNatural: true },
} as const

for (const viewport of viewports) {
  test(`Baccarat ${viewport.name} fits betting, four/five/six-card hands and optional panels`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    const fixture = await mockBaccarat(page, { hands: ['natural-banker', 'banker-third', 'both-third'] })
    await page.goto('/games/baccarat')
    const stake = page.getByRole('spinbutton', { name: 'Stake', exact: true })
    const deal = page.getByRole('button', { name: 'Deal', exact: true })
    await expect(stake).toBeEnabled()
    await page.evaluate(() => document.fonts.ready)
    await stake.fill('')
    await expect(stake).toHaveValue('')
    await expect(deal).toBeDisabled()
    await page.getByRole('button', { name: 'Increase Baccarat stake' }).click()
    await expect(stake).toHaveValue('1')
    await stake.fill('10.5')
    await expect(deal).toBeDisabled()
    await expect(stake).toHaveAttribute('aria-invalid', 'true')
    await expect(page.locator('.ff-baccarat__stake small')).toBeVisible()
    await expect(page.locator('.ff-baccarat__stake small')).toContainText('increments')
    await page.getByRole('button', { name: 'Decrease Baccarat stake' }).click()
    await expect(stake).toHaveValue('10')
    await page.getByRole('button', { name: /^Banker/ }).click()
    await fits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-betting`)
    const board = await page.locator('.ff-baccarat__hands').boundingBox()
    await deal.click()
    await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
    await expect(page.getByRole('button', { name: 'Dealing…', exact: true })).toHaveCount(0)
    await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R1,009.50')
    await expect(page.getByRole('button', { name: 'Set Baccarat stake to R25.00' })).toBeDisabled()
    await expect(page.locator('.ff-baccarat__round-head')).toHaveText('NaturalBanker wins+R9.50')
    await expect(page.getByLabel('eight of spades', { exact: true })).toHaveCount(2)
    expect(fixture.requests[0].body).toEqual({ betSide: 'banker', stake: 10 })
    expect(await page.locator('.ff-baccarat__hands').boundingBox()).toEqual(board)
    await expect(page.locator('.ff-baccarat__card').first()).toHaveCSS('animation-name', 'none')
    await fits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-natural-result`)

    await page.getByRole('button', { name: 'Baccarat round details', exact: true }).click()
    const details = page.getByRole('dialog', { name: 'Baccarat round details', exact: true })
    await expect(details).toContainText('Profit+R9.50Total returnR19.50')
    await fitsPanel(details, viewport)
    await capture(page, testInfo, `${viewport.name}-details`)
    await page.keyboard.press('Escape')
    await expect(page.getByRole('button', { name: 'Baccarat round details', exact: true })).toBeFocused()

    await page.getByRole('button', { name: 'Deal Again', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
    await expect(page.getByLabel('Banker hand, total 7', { exact: true }).getByLabel('two of hearts', { exact: true })).toBeVisible()
    await expect(page.locator('.ff-baccarat__card:not(.ff-baccarat__card--back)')).toHaveCount(5)
    expect(fixture.requests[1].body).toEqual(fixture.requests[0].body)
    expect(fixture.requests[1].key).not.toBe(fixture.requests[0].key)
    await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R1,019.00')
    expect(await page.locator('.ff-baccarat__hands').boundingBox()).toEqual(board)
    await fits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-banker-third-result`)

    await page.getByRole('button', { name: 'New Bet', exact: true }).click()
    await expect(stake).toHaveValue('10')
    await expect(stake).toBeFocused()
    await page.getByRole('button', { name: /^Player/ }).click()
    await stake.fill('5')
    await deal.click()
    await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
    await expect(page.locator('.ff-baccarat__card:not(.ff-baccarat__card--back)')).toHaveCount(6)
    await expect(page.getByLabel('Player hand, total 9', { exact: true }).getByLabel('six of spades', { exact: true })).toBeVisible()
    await expect(page.getByLabel('Banker hand, total 7', { exact: true }).getByLabel('three of diamonds', { exact: true })).toBeVisible()
    await expect(page.locator('.ff-baccarat__round-head')).toHaveText('Player wins+R5.00')
    await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R1,024.00')
    expect(fixture.requests[2].body).toEqual({ betSide: 'player', stake: 5 })
    await fits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-six-card-result`)
    await page.getByRole('button', { name: 'Baccarat road and shoe', exact: true }).click()
    const road = page.getByRole('dialog', { name: 'Baccarat road and shoe', exact: true })
    // Shoe remaining, not used: 416 - (4 + 5 + 6).
    await expect(road).toContainText('401 cards')
    await expect(road.locator('.ff-baccarat__shoe-progress')).toBeVisible()
    expect(await road.locator('.ff-baccarat__shoe-progress').evaluate(e => getComputedStyle(e).position)).toBe('static')
    await expect(road.locator('.ff-baccarat__shoe-progress')).toHaveCSS('transform', 'none')
    await expect(road.locator('ol li')).toHaveCount(3)
    await fitsPanel(road, viewport)
    await capture(page, testInfo, `${viewport.name}-road`)
    await page.keyboard.press('Escape')
    await expect(page.getByRole('button', { name: 'Baccarat road and shoe', exact: true })).toBeFocused()
    await page.getByRole('button', { name: 'How to play Baccarat', exact: true }).click()
    const help = page.getByRole('dialog', { name: 'How to play Baccarat', exact: true })
    await expect(help).toContainText('5% commission')
    await expect(page.getByRole('button', { name: 'Close how to play baccarat', exact: true })).toBeFocused()
    await fitsPanel(help, viewport)
    await capture(page, testInfo, `${viewport.name}-help`)
    await page.keyboard.press('Escape')
    await expect(page.getByRole('button', { name: 'How to play Baccarat', exact: true })).toBeFocused()
  })
}

test('Baccarat handles Player third card, Tie push, 8:1 Tie win and loss without changing settlement', async ({ page }, testInfo) => {
  const fixture = await mockBaccarat(page, { hands: ['player-third-tie', 'natural-tie', 'natural-player'] })
  await page.goto('/games/baccarat')
  await page.getByRole('spinbutton', { name: 'Stake' }).fill('10')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
  await expect(page.getByLabel('Player hand, total 7', { exact: true }).getByLabel('four of spades', { exact: true })).toBeVisible()
  await expect(page.locator('.ff-baccarat__round-head')).toHaveText('TieR0.00')
  await expect(page.locator('.ff-baccarat__hand.is-winner')).toHaveCount(2)
  await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R1,000.00')
  await capture(page, testInfo, 'player-third-tie-push')
  await page.getByRole('button', { name: 'New Bet', exact: true }).click()
  await page.getByRole('button', { name: /^Tie/ }).click()
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.locator('.ff-baccarat__round-head')).toHaveText('NaturalTie+R80.00')
  await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R1,080.00')
  await capture(page, testInfo, 'natural-tie-win')
  await page.getByRole('button', { name: 'Deal Again', exact: true }).click()
  await expect(page.locator('.ff-baccarat__round-head')).toHaveText('NaturalPlayer wins-R10.00')
  await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R1,070.00')
  expect(fixture.requests[2].body).toEqual({ betSide: 'tie', stake: 10 })
  await capture(page, testInfo, 'natural-player-loss')
})

test('Baccarat locks an uncertain wager, replays its key on reload and never duplicates a stored hand', async ({ page }, testInfo) => {
  const fixture = await mockBaccarat(page, { failDeal: true, hands: ['banker-third'], shoeMetadata: 'remaining' })
  await page.goto('/games/baccarat')
  await page.getByRole('button', { name: /^Banker/ }).click()
  await page.getByRole('spinbutton', { name: 'Stake' }).fill('10')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Retry Deal', exact: true })).toBeEnabled()
  await expect(page.getByRole('spinbutton', { name: 'Stake' })).toBeDisabled()
  await expect(page.getByRole('button', { name: /^Player/ })).toBeDisabled()
  await page.reload()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
  expect(fixture.requests.slice(1).every(request => JSON.stringify(request) === JSON.stringify(fixture.requests[0]))).toBe(true)
  await expect(page.getByRole('alert')).toHaveCount(0)
  // Simulate receiving the same completed request again after a reload.
  await page.evaluate(({ key, roundId }) => {
    sessionStorage.setItem('fortuneforge:baccarat:pending:baccarat-ux-player:account', JSON.stringify({ idempotencyKey: key, betSide: 'banker', stake: 10 }))
    localStorage.setItem('fortuneforge:baccarat:shoe:baccarat-ux-player:account', '5')
    if (!localStorage.getItem('fortuneforge:baccarat:history:baccarat-ux-player:account')?.includes(roundId)) throw new Error('No recorded hand')
  }, { key: fixture.requests[0].key!, roundId: 'baccarat-ux-1' })
  await page.reload()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: 'Baccarat road and shoe', exact: true }).click()
  const road = page.getByRole('dialog', { name: 'Baccarat road and shoe', exact: true })
  await expect(road).toContainText('Live shoe411 cards')
  await expect(road.locator('ol li')).toHaveCount(1)
  await capture(page, testInfo, 'restored-known-round-live-shoe')
})

test('Baccarat independently retries failed status and restoration in short landscape', async ({ page }, testInfo) => {
  const viewport = { width: 667, height: 375 }
  await page.setViewportSize(viewport)
  const fixture = await mockBaccarat(page, { failDeal: true, blockRestoration: true })
  await page.goto('/games/baccarat')
  await page.getByRole('spinbutton', { name: 'Stake' }).fill('10')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Retry Deal', exact: true })).toBeEnabled()
  fixture.blockStatus()
  await page.reload()
  await expect(page.getByRole('button', { name: 'Retry restoration', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Retry connection', exact: true })).toBeEnabled()
  await fits(page, viewport)
  await capture(page, testInfo, 'landscape-failed-status-and-restoration')
  fixture.releaseRestoration()
  await page.getByRole('button', { name: 'Retry restoration', exact: true }).click()
  await expect(page.getByRole('button', { name: 'New Bet', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Retry connection', exact: true })).toBeEnabled()
  await expect(page.getByRole('alert')).toContainText('Offline')
  await fits(page, viewport)
  await capture(page, testInfo, 'landscape-restored-hand-offline-status')
  fixture.releaseStatus()
  await page.getByRole('button', { name: 'Retry connection', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
  await expect(page.getByRole('alert')).toHaveCount(0)
  await fits(page, viewport)
  expect(fixture.requests.every(request => request.key === fixture.requests[0].key)).toBe(true)
})

test('Baccarat fits failed initial status and does not offer retries on an intentionally closed table', async ({ page }, testInfo) => {
  const viewport = { width: 320, height: 568 }
  await page.setViewportSize(viewport)
  const fixture = await mockBaccarat(page, { blockStatus: true })
  await page.goto('/games/baccarat')
  await expect(page.getByRole('button', { name: 'Retry connection', exact: true })).toBeEnabled()
  await fits(page, viewport)
  await capture(page, testInfo, 'compact-phone-failed-status')
  fixture.releaseStatus()
  await page.getByRole('button', { name: 'Retry connection', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeEnabled()
  fixture.closeTable()
  await page.reload()
  await expect(page.getByRole('alert')).toContainText('temporarily unavailable')
  await expect(page.getByRole('button', { name: 'Retry connection', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled()
  await fits(page, viewport)
  await capture(page, testInfo, 'compact-phone-closed-table')
})

test('Baccarat uses nonstandard increments and latest returned balance for affordability', async ({ page }, testInfo) => {
  const fixture = await mockBaccarat(page, { balance: 8, limits: { minimumStake: 2, maximumStake: 20, stakeIncrement: 3 }, hands: ['natural-banker'] })
  await page.goto('/games/baccarat')
  const stake = page.getByRole('spinbutton', { name: 'Stake' })
  await expect(stake).toHaveValue('2')
  await page.getByRole('button', { name: 'Increase Baccarat stake' }).click()
  await expect(stake).toHaveValue('5')
  await stake.fill('6')
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: 'Decrease Baccarat stake' }).click()
  await expect(stake).toHaveValue('5')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeDisabled()
  await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R3.00')
  await page.getByRole('button', { name: 'New Bet', exact: true }).click()
  await expect(stake).toHaveValue('5')
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: 'Decrease Baccarat stake' }).click()
  await expect(stake).toHaveValue('2')
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeEnabled()
  expect(fixture.requests).toHaveLength(1)
  await capture(page, testInfo, 'latest-balance-affordable-next-bet')
})

async function fits(page: Page, viewport: { width: number; height: number }) {
  expect(await page.evaluate(() => Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))).toBeLessThanOrEqual(viewport.height + 1)
  expect(await page.locator('.in-game-shell__body').evaluate(e => e.scrollHeight <= e.clientHeight + 1)).toBe(true)
  if (viewport.height <= 500 && viewport.width > viewport.height) {
    const table = (await page.locator('.ff-baccarat__table').boundingBox())!
    const surface = (await page.locator('.ff-baccarat__play-surface').boundingBox())!
    expect(surface.x).toBeCloseTo(table.x, 0)
    expect(surface.width).toBeCloseTo(table.width, 0)
  }
  for (const surface of await page.locator('.ff-baccarat__table, .ff-baccarat__card, .ff-baccarat__rail, .ff-baccarat button, .ff-baccarat input').all()) {
    const bounds = await surface.boundingBox()
    expect(bounds).not.toBeNull()
    expect(bounds!.x).toBeGreaterThanOrEqual(-1)
    expect(bounds!.y).toBeGreaterThanOrEqual(-1)
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width + 1)
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height + 1)
    if (await surface.evaluate(e => e.tagName === 'BUTTON')) expect(bounds!.height).toBeGreaterThanOrEqual(44)
  }
  for (const hand of await page.locator('.ff-baccarat__hand').all()) {
    const handBox = (await hand.boundingBox())!
    const titleBox = (await hand.locator('.ff-baccarat__hand-title').boundingBox())!
    for (const card of await hand.locator('.ff-baccarat__card').all()) {
      const cardBox = (await card.boundingBox())!
      expect(cardBox.y).toBeGreaterThanOrEqual(titleBox.y + titleBox.height - 1)
      expect(cardBox.y + cardBox.height).toBeLessThanOrEqual(handBox.y + handBox.height + 1)
    }
  }
  const result = page.locator('.ff-baccarat__round-head')
  if (await result.innerText()) {
    const resultBox = (await result.boundingBox())!
    const rackBox = (await page.locator('.ff-baccarat__chips').boundingBox())!
    expect(resultBox.x).toBeGreaterThanOrEqual(rackBox.x + rackBox.width)
    expect(resultBox.x + resultBox.width).toBeLessThanOrEqual(viewport.width + 1)
  }
  expect(await page.locator('.ff-baccarat').evaluate(e => getComputedStyle(e).userSelect)).toBe('none')
}
async function fitsPanel(panel: Locator, viewport: { width: number; height: number }) {
  const box = await panel.boundingBox()
  expect(box!.x).toBeGreaterThanOrEqual(0)
  expect(box!.y).toBeGreaterThanOrEqual(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width + 1)
  expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height + 1)
}
async function capture(page: Page, testInfo: TestInfo, name: string) {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, animations: 'disabled', caret: 'hide' })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}
async function mockBaccarat(page: Page, options: {
  balance?: number; hands?: HandKind[]; failDeal?: boolean; blockRestoration?: boolean; blockStatus?: boolean;
  limits?: Partial<BaccaratStatus>; shoeMetadata?: 'remaining' | 'none';
} = {}) {
  let balance = options.balance ?? 1_000, count = 0, shoeUsed = 0
  let failedDeal = false, blockedRestoration = Boolean(options.blockRestoration), blockedStatus = Boolean(options.blockStatus), closed = false
  const requests: { body: { betSide: BaccaratBetSide; stake: number }; key: string | undefined }[] = []
  const completed = new Map<string | undefined, BaccaratRound>()
  await page.route('**/api/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname
    if (path === '/api/accounts/me') return route.fulfill({ json: { ...account, balances: { ...account.balances, slotsCredits: balance } } })
    if (path.endsWith('/status')) {
      if (blockedStatus) return route.fulfill({ status: 503, json: { code: 'temporary', message: 'Offline' } })
      return route.fulfill({ json: { ...status, ...options.limits, balance, available: !closed } })
    }
    if (request.method() === 'POST' && path.endsWith('/rounds')) {
      const body = request.postDataJSON() as { betSide: BaccaratBetSide; stake: number }
      const key = request.headers()['idempotency-key']
      requests.push({ body, key })
      if (completed.has(key)) return route.fulfill({ json: completed.get(key) })
      if (options.failDeal && (!failedDeal || blockedRestoration)) {
        failedDeal = true
        return route.fulfill({ status: 503, json: { code: 'temporary', message: 'Connection interrupted' } })
      }
      const hand = hands[(options.hands ?? ['natural-banker'])[count] ?? 'natural-banker']
      const disposition = body.betSide === hand.outcome ? 'win' : hand.outcome === 'tie' && body.betSide !== 'tie' ? 'push' : 'loss'
      const profit = disposition === 'win' ? body.stake * (body.betSide === 'banker' ? .95 : body.betSide === 'tie' ? 8 : 1) : disposition === 'push' ? 0 : -body.stake
      balance = Math.round((balance + profit) * 100) / 100
      count++
      shoeUsed += hand.playerCards.length + hand.bankerCards.length
      const round: BaccaratRound = { ...hand, roundId: `baccarat-ux-${count}`, balance, betSide: body.betSide, stake: body.stake, phase: 'settled', disposition, profit, totalReturn: body.stake + profit, shoeCardsUsed: shoeUsed, shoeCardsRemaining: 416 - shoeUsed }
      if (options.shoeMetadata === 'remaining' || options.shoeMetadata === 'none') delete (round as { shoeCardsUsed?: number }).shoeCardsUsed
      if (options.shoeMetadata === 'none') delete (round as { shoeCardsRemaining?: number }).shoeCardsRemaining
      completed.set(key, round)
      return route.fulfill({ json: round })
    }
    return route.fulfill({ status: 404, json: { message: 'Unexpected endpoint' } })
  })
  return { requests, releaseRestoration: () => { blockedRestoration = false }, blockStatus: () => { blockedStatus = true }, releaseStatus: () => { blockedStatus = false }, closeTable: () => { closed = true } }
}

test('Baccarat keeps the same table on reload and resets after leaving', async ({ page }) => {
  test.setTimeout(60_000)
  await page.emulateMedia({ reducedMotion: 'reduce' })
  const options = {
    hands: ['natural-player'] as HandKind[],
    limits: { minimumStake: .25, stakeIncrement: .25, supportsPairBets: true },
  }
  const fixture = await mockBaccarat(page, options)
  await page.goto('/games/baccarat')
  const player = page.locator('.ff-baccarat__bet.is-player')
  const pair = page.locator('.ff-baccarat__bet.is-player-pair')
  const chosenChip = page.getByRole('button', { name: 'Select R5.00 chip', exact: true })
  const roadTotal = page.locator('.ff-baccarat__inline-road .ff-baccarat__road-stats span').filter({ hasText: /^Total/ }).locator('b')
  const shoe = page.locator('.ff-baccarat__inline-road .ff-baccarat__shoe-progress strong')
  const chipSources = () => page.locator('.ff-baccarat__bet .ff-baccarat__chip-pile img')
    .evaluateAll(chips => chips.map(chip => chip.getAttribute('src')))
  const cardLabels = () => page.locator('.ff-baccarat__cards .ff-playing-card--face')
    .evaluateAll(cards => cards.map(card => card.getAttribute('aria-label')))

  await expect(player).toBeEnabled()
  await chosenChip.click()
  await player.click()
  await player.click()
  await pair.click()
  const placedChips = await chipSources()
  expect(placedChips).toHaveLength(3)
  expect(new Set(placedChips).size).toBe(1)
  // Keep native navigation timing intact: Playwright's clock also replaces
  // performance entries, which are how the table distinguishes a reload.
  await page.addInitScript(now => { Date.now = () => now }, Date.now() + 7 * 24 * 60 * 60 * 1000)
  await page.reload()
  await expect(player.locator('.ff-baccarat__placed-chip em')).toHaveText('R10.00')
  await expect(pair).toHaveAttribute('aria-pressed', 'true')
  await expect(chosenChip).toHaveAttribute('aria-pressed', 'true')
  await expect(page.locator('.ff-baccarat__table-totals > span').first()).toHaveText('Bet R15.00')
  expect(await chipSources()).toEqual(placedChips)
  expect(fixture.requests).toHaveLength(0)

  // Pair placement persistence is covered above; the shared round fixture settles main wagers.
  await page.getByRole('button', { name: 'Clear Baccarat bets', exact: true }).click()
  await player.click()
  await player.click()
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Baccarat result', exact: true })).toBeVisible()
  await expect(roadTotal).toHaveText('1')
  await expect(shoe).toHaveText('412 cards')
  const dealtCards = await cardLabels()
  expect(dealtCards).toHaveLength(4)
  await page.reload()
  await expect(page.getByRole('dialog', { name: 'Baccarat result', exact: true })).toBeVisible()
  expect(await cardLabels()).toEqual(dealtCards)
  await expect(roadTotal).toHaveText('1')
  await expect(shoe).toHaveText('412 cards')
  expect(fixture.requests).toHaveLength(1)

  await page.getByRole('button', { name: 'Continue', exact: true }).click()
  await page.reload()
  await expect(player).toBeEnabled()
  await expect(page.getByRole('dialog', { name: 'Baccarat result', exact: true })).toHaveCount(0)
  await expect(roadTotal).toHaveText('1')
  await expect(shoe).toHaveText('412 cards')
  expect(await chipSources()).toHaveLength(0)
  expect(fixture.requests).toHaveLength(1)

  const freshPage = await page.context().newPage()
  try {
    await mockBaccarat(freshPage, options)
    await freshPage.goto(page.url())
    await expect(freshPage.locator('.ff-baccarat__bet.is-player')).toBeEnabled()
    await expect(freshPage.locator('.ff-baccarat__inline-road .ff-baccarat__road-stats')).toContainText(/Total\s*0/)
    await expect(freshPage.locator('.ff-baccarat__inline-road .ff-baccarat__shoe-progress strong')).toHaveText('416 cards')
    await expect(freshPage.locator('.ff-baccarat__bet .ff-baccarat__chip-pile img')).toHaveCount(0)
  } finally { await freshPage.close() }
  await expect(roadTotal).toHaveText('1')
  await expect(shoe).toHaveText('412 cards')

  await page.getByRole('link', { name: 'Other Games', exact: true }).click()
  await expect(page).toHaveURL(/\/home(?:#|$)/)
  await page.goto('/games/baccarat')
  await expect(player).toBeEnabled()
  await expect(roadTotal).toHaveText('0')
  await expect(shoe).toHaveText('416 cards')
  expect(await chipSources()).toHaveLength(0)
})

test('Baccarat keeps the full betting board above its bottom bar while resizing', async ({ page }) => {
  test.setTimeout(90_000)
  const sizes = [
    { width: 1920, height: 960 }, { width: 1280, height: 720 }, { width: 1280, height: 600 },
    { width: 1024, height: 768 }, { width: 768, height: 1024 },
    { width: 390, height: 844 }, { width: 320, height: 568 }, { width: 375, height: 667 },
    { width: 844, height: 390 }, { width: 667, height: 375 },
  ]
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await mockBaccarat(page, {
    balance: 138_506.46, hands: ['natural-player'],
    limits: { minimumStake: .25, stakeIncrement: .25, supportsPairBets: true },
  })
  await page.goto('/games/baccarat')
  const player = page.locator('.ff-baccarat__bet.is-player')
  await expect(player).toBeEnabled()
  await page.evaluate(() => document.fonts.ready)
  await player.click()
  await expect(player.locator('.ff-baccarat__placed-chip em')).toHaveText('R0.25')

  const checkResizing = async (phase: string) => {
    for (const viewport of sizes) {
      await test.step(`${phase} at ${viewport.width}×${viewport.height}`, async () => {
        await page.setViewportSize(viewport)
        const geometry = await page.evaluate(() => {
          const rect = (element: Element) => {
            const { left, right, top, bottom, width, height } = element.getBoundingClientRect()
            return { left, right, top, bottom, width, height }
          }
          const surface = rect(document.querySelector('.ff-baccarat__play-surface')!)
          const rail = rect(document.querySelector('.ff-baccarat__rail')!)
          const boardContent = Array.from(document.querySelectorAll(
            '.ff-baccarat__bet > strong, .ff-baccarat__bet > span:not(.ff-baccarat__placed-chip), .ff-baccarat__bet.is-player .ff-baccarat__placed-chip img, .ff-baccarat__bet.is-player .ff-baccarat__placed-chip em',
          ), (element, index) => ({ name: `${element.className || element.textContent} [${index}]`, ...rect(element) }))
          const contentBoxes = (selector: string) => Array.from(document.querySelectorAll(selector),
            (element) => ({ name: element.getAttribute('aria-label') || element.textContent, ...rect(element) }))
            .filter(box => box.width > 0 && box.height > 0)
          const railContent = contentBoxes('.ff-baccarat__rail button, .ff-baccarat__table-totals > span')
          const floatingContent = contentBoxes('.game-support-controls button' +
            (matchMedia('(max-height: 500px) and (orientation: landscape)').matches ? ', .ff-baccarat__header button' : ''))
          return { surface, rail, boardContent, railContent, floatingContent, width: innerWidth, height: innerHeight, scrollWidth: document.documentElement.scrollWidth }
        })
        const issues: string[] = []
        for (const box of geometry.boardContent) {
          if (box.left < geometry.surface.left - 1 || box.right > geometry.surface.right + 1
            || box.top < geometry.surface.top - 1 || box.bottom > geometry.surface.bottom + 1
            || box.bottom > geometry.rail.top + 1) issues.push(`${box.name} is clipped by the board or bottom bar`)
        }
        for (const box of geometry.railContent) {
          if (box.left < geometry.rail.left - 1 || box.right > geometry.rail.right + 1
            || box.top < geometry.rail.top - 1 || box.bottom > geometry.rail.bottom + 1
            || box.bottom > geometry.height + 1) issues.push(`${box.name} is clipped in the bottom bar`)
        }
        for (const box of geometry.floatingContent) {
          if (box.left < -1 || box.right > geometry.width + 1 || box.top < -1 || box.bottom > geometry.height + 1) {
            issues.push(`${box.name} is clipped by the viewport`)
          }
        }
        const bottomContent = [...geometry.railContent, ...geometry.floatingContent]
        for (let index = 0; index < bottomContent.length; index++) {
          const a = bottomContent[index]
          for (const b of bottomContent.slice(index + 1)) {
            if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1
              && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1) issues.push(`${a.name} overlaps ${b.name}`)
          }
        }
        expect(geometry.surface.bottom).toBeLessThanOrEqual(geometry.rail.top + 1)
        expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.width)
        expect(issues).toEqual([])
      })
    }
  }

  await checkResizing('betting')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Baccarat result' })).toBeVisible()
  await checkResizing('settled')
  await page.getByRole('button', { name: 'Continue', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled()
  await checkResizing('next bet')
})

test('Baccarat survives reload during the normal card reveal and unlocks after the last entrance', async ({ page }, testInfo) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await page.addInitScript(() => {
    const frames: { time: number; cards: number; completed: boolean }[] = []
    Reflect.set(window, 'baccaratFrames', frames)
    new MutationObserver(() => {
      const main = document.querySelector('.ff-baccarat')
      if (!main) return
      const cards = main.querySelectorAll('.ff-baccarat__card:not(.ff-baccarat__card--back)').length
      const completed = [...main.querySelectorAll('button')].some(button => button.textContent === 'Deal Again')
      if (frames.at(-1)?.cards !== cards || frames.at(-1)?.completed !== completed) frames.push({ time: performance.now(), cards, completed })
    }).observe(document, { childList: true, subtree: true })
  })
  const fixture = await mockBaccarat(page, { hands: ['banker-third'] })
  await page.goto('/games/baccarat')
  await page.getByRole('button', { name: /^Banker/ }).click()
  await page.getByRole('spinbutton', { name: 'Stake' }).fill('10')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('img', { name: 'three of clubs', exact: true })).toBeVisible()
  expect(await page.evaluate(() => sessionStorage.getItem('fortuneforge:baccarat:pending:baccarat-ux-player:account'))).not.toBeNull()
  await page.reload()
  await expect(page.getByRole('img', { name: 'two of hearts', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
  const frames = await page.evaluate(() => Reflect.get(window, 'baccaratFrames')) as { time: number; cards: number; completed: boolean }[]
  const lastCard = frames.find(frame => frame.cards === 5 && !frame.completed)!
  const unlocked = frames.find(frame => frame.cards === 5 && frame.completed)!
  expect(lastCard).toBeTruthy()
  expect(unlocked.time - lastCard.time).toBeGreaterThanOrEqual(250)
  await expect(page.getByRole('img', { name: 'two of hearts', exact: true })).toHaveCSS('animation-name', 'ff-baccarat-card-deal')
  await expect(page.getByRole('img', { name: 'two of hearts', exact: true })).toHaveCSS('opacity', '1')
  expect(await page.evaluate(() => sessionStorage.getItem('fortuneforge:baccarat:pending:baccarat-ux-player:account'))).toBeNull()
  expect(fixture.requests.length).toBeGreaterThanOrEqual(2)
  expect(fixture.requests.every(request => JSON.stringify(request) === JSON.stringify(fixture.requests[0]))).toBe(true)
  await page.getByRole('button', { name: 'Baccarat road and shoe', exact: true }).click()
  const road = page.getByRole('dialog', { name: 'Baccarat road and shoe', exact: true })
  await expect(road.locator('ol li')).toHaveCount(1)
  await expect(road).toContainText('411 cards')
  await capture(page, testInfo, 'reload-during-reveal-restored')
})

test('Baccarat keyboard opens one panel at a time and returns focus on Escape', async ({ page }) => {
  await mockBaccarat(page)
  await page.goto('/games/baccarat')
  const road = page.getByRole('button', { name: 'Baccarat road and shoe', exact: true })
  const help = page.getByRole('button', { name: 'How to play Baccarat', exact: true })
  await road.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('dialog', { name: 'Baccarat road and shoe', exact: true })).toBeVisible()
  await page.keyboard.press('Shift+Tab')
  await expect(road).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Close baccarat road and shoe', exact: true })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(help).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await expect(page.getByRole('dialog', { name: 'How to play Baccarat', exact: true })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(help).toBeFocused()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('Baccarat session estimate finishes a hand across the cut, survives reload and resets before the next hand', async ({ page }, testInfo) => {
  await page.addInitScript(() => {
    if (!localStorage.getItem('fortuneforge:baccarat:shoe:baccarat-ux-player:account')) localStorage.setItem('fortuneforge:baccarat:shoe:baccarat-ux-player:account', '311')
  })
  await mockBaccarat(page, { hands: ['banker-third', 'natural-banker'], shoeMetadata: 'none' })
  await page.goto('/games/baccarat')
  await page.getByRole('button', { name: /^Banker/ }).click()
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
  await expect(page.locator('.ff-baccarat__inline-road')).toContainText('Session estimate100 cards')
  expect(await page.evaluate(() => localStorage.getItem('fortuneforge:baccarat:shoe:baccarat-ux-player:account'))).toBe('316')
  await page.reload()
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeEnabled()
  await expect(page.locator('.ff-baccarat__inline-road')).toContainText('Session estimate100 cards')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
  await expect(page.locator('.ff-baccarat__inline-road')).toContainText('Session estimate412 cards')
  expect(await page.evaluate(() => localStorage.getItem('fortuneforge:baccarat:shoe:baccarat-ux-player:account'))).toBe('4')
  await capture(page, testInfo, 'estimated-shoe-reshuffle')
})

test('Baccarat compact Road keeps the newest opposite result and all twelve columns inside its panel', async ({ page }, testInfo) => {
  const viewport = { width: 320, height: 568 }
  await page.setViewportSize(viewport)
  await page.addInitScript(() => {
    const history = Array.from({ length: 30 }, (_, index) => ({ roundId: 'old-' + index, outcome: 'banker', cardsUsed: 4, natural: false }))
    history.push({ roundId: 'latest-player', outcome: 'player', cardsUsed: 4, natural: false })
    localStorage.setItem('fortuneforge:baccarat:history:baccarat-ux-player:account', JSON.stringify(history.reverse()))
  })
  await mockBaccarat(page)
  await page.goto('/games/baccarat')
  await page.getByRole('button', { name: 'Baccarat road and shoe', exact: true }).click()
  const road = page.getByRole('dialog', { name: 'Baccarat road and shoe', exact: true })
  await fitsPanel(road, viewport)
  await expect(road.locator('.ff-baccarat__big-road i').last()).toHaveClass('is-player')
  const gridBox = (await road.locator('.ff-baccarat__big-road > div').boundingBox())!
  for (const cell of await road.locator('.ff-baccarat__big-road i').all()) {
    const box = (await cell.boundingBox())!
    expect(box.x).toBeGreaterThanOrEqual(gridBox.x)
    expect(box.x + box.width).toBeLessThanOrEqual(gridBox.x + gridBox.width)
  }
  await capture(page, testInfo, 'compact-phone-long-streak-road')
})

test('Baccarat keeps every roadmap and shoe accessible in a short desktop window', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 600 })
  await page.addInitScript(() => {
    localStorage.setItem('fortuneforge:baccarat:history:baccarat-ux-player:account', JSON.stringify(
      Array.from({ length: 120 }, (_, index) => ({ roundId: 'history-' + index, outcome: index % 3 === 0 ? 'player' : 'banker', cardsUsed: 4, natural: false })),
    ))
  })
  await mockBaccarat(page)
  await page.goto('/games/baccarat')
  const history = page.getByRole('region', { name: 'Recent Baccarat history' })
  await expect(history).toBeVisible()
  await history.focus()
  await page.keyboard.press('End')
  const shoe = history.locator('.ff-baccarat__shoe-progress')
  await expect(shoe).toBeInViewport()
  const shoeBox = (await shoe.boundingBox())!
  const historyBox = (await history.boundingBox())!
  expect(shoeBox.y + shoeBox.height).toBeLessThanOrEqual(historyBox.y + historyBox.height + 1)
  await capture(page, testInfo, 'short-desktop-roadmap-scroll')
})

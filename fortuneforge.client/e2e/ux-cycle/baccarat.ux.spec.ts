import { expect, test, type Page, type TestInfo, type Locator } from '@playwright/test'
import type { BaccaratBetSide, BaccaratCard, BaccaratRound, BaccaratStatus } from '../../../game-packages/games/Baccarat/client/FortuneForge.Games.Baccarat.Client/src/contracts'
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
    await page.getByRole('button', { name: 'Decrease Baccarat stake' }).click()
    await expect(stake).toHaveValue('10')
    await page.getByRole('button', { name: /^Banker/ }).click()
    await fits(page, viewport)
    await capture(page, testInfo, `${viewport.name}-betting`)
    const board = await page.locator('.ff-baccarat__hands').boundingBox()
    await deal.click()
    await expect(page.getByRole('button', { name: 'Dealing…', exact: true })).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toHaveCount(0)
    await expect(page.locator('.ff-baccarat__round-head')).toBeEmpty()
    await expect(page.locator('.ff-baccarat__account strong')).toHaveText('R1,000.00')
    await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
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

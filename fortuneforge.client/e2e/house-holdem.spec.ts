import { expect, test, type Page } from '@playwright/test'

const card = (rank: string, suit: string) => ({ rank, suit })
const board = [card('two', 'hearts'), card('three', 'clubs'), card('four', 'diamonds'), card('five', 'spades'), card('six', 'spades')]

for (const viewport of [
  { name: 'desktop', width: 1280, height: 720 },
  { name: 'mobile', width: 390, height: 844 },
  { name: 'small-phone', width: 320, height: 667 },
  { name: 'compact-phone', width: 375, height: 667 },
  { name: 'landscape', width: 844, height: 390 },
] as const) {
  test(`House Hold’em remains usable in a ${viewport.name} viewport`, async ({ page }) => {
    await page.setViewportSize(viewport)
    const fixture = await mockHouseHoldem(page)
    await page.goto('/cards/house-holdem')
    await expect(page.getByRole('button', { name: 'Place chip on Ante' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Place chip on Trips' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled()
    await page.getByRole('button', { name: 'Place chip on Ante' }).click()
    await expect(page.locator('.three-card-poker-action-stats span').first()).toContainText('R2.00')
    await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeEnabled()
    await expectNoOverflow(page, viewport.width, viewport.height)
    await page.screenshot({ path: `../artifacts/house-holdem-ui/${viewport.name}-betting.png` })
    await page.getByRole('button', { name: 'Deal', exact: true }).click()
    await expect(page.locator('.blackjack-table')).toHaveAttribute('data-phase', 'dealing')
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Wager 4 times your Ante' })).toBeVisible()
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(2)
    await expect(page.locator('.ff-playing-card--back')).toHaveCount(2)
    await expect(page.locator('.house-holdem-board .three-card-poker-card-outline')).toHaveCount(5)
    await expectNoOverflow(page, viewport.width, viewport.height)
    await expectNoCardOverlap(page, viewport.name === 'landscape')
    await page.screenshot({ path: `../artifacts/house-holdem-ui/${viewport.name}-preflop.png` })
    await page.getByRole('button', { name: 'Wager 4 times your Ante' }).click()
    await expect(page.locator('.blackjack-table')).toHaveAttribute('data-phase', 'flop')
    await expect(page.locator('.house-holdem-board .ff-playing-card--face')).toHaveCount(3)
    await expect(page.locator('.ff-playing-card--back')).toHaveCount(2)
    await expectNoOverflow(page, viewport.width, viewport.height)
    await page.screenshot({ path: `../artifacts/house-holdem-ui/${viewport.name}-flop.png` })
    await expect(page.locator('.blackjack-table')).toHaveAttribute('data-phase', 'settled', { timeout: 8_000 })
    await expect(page.locator('.ff-playing-card--back')).toHaveCount(0)
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(9)
    await expect(page.getByRole('status', { name: 'Round result' })).toBeVisible()
    await expect(page.locator('.three-card-poker-action-stats span').first()).toContainText('R6.00')
    await expectNoOverflow(page, viewport.width, viewport.height)
    await page.screenshot({ path: `../artifacts/house-holdem-ui/${viewport.name}-settled.png` })
    if (viewport.name === 'desktop') await page.getByRole('button', { name: 'Place chip on Ante' }).click()
    else await page.getByRole('status', { name: 'Round result' }).click()
    await expect(page.getByRole('status', { name: 'Round result' })).toHaveCount(0)
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(0)
    await expect(page.locator('.ff-playing-card--back')).toHaveCount(0)
    await expect(page.locator('.three-card-poker-card-outline')).toHaveCount(9)
    await expect(page.locator('.three-card-poker-action-stats span').first()).toContainText('R0.00')
    await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled()
    await expect(page.locator('.house-holdem-bet-spot--ante .three-card-poker-chip')).toHaveCount(0)
    expect(fixture.start).toEqual([{ ante: 1, trips: 0 }])
    expect(fixture.actions).toEqual(['play-4x'])
    expect(fixture.advances).toBe(2)
  })
}

test('House Hold’em deals both house cards before either player card', async ({ page }) => {
  await mockHouseHoldem(page)
  await page.goto('/cards/house-holdem')
  await page.getByRole('button', { name: 'Place chip on Ante' }).click()
  await page.evaluate(() => {
    const trackedWindow = window as Window & { houseDealTrace?: string[] }
    trackedWindow.houseDealTrace = []
    const record = () => {
      const house = document.querySelectorAll('.blackjack-dealer .ff-playing-card--back').length
      const player = document.querySelectorAll('.three-card-poker-player .ff-playing-card--back').length
      const state = `${house}:${player}`
      if (trackedWindow.houseDealTrace?.at(-1) !== state) trackedWindow.houseDealTrace?.push(state)
    }
    const table = document.querySelector('.blackjack-table')
    if (table) new MutationObserver(record).observe(table, { childList: true, subtree: true })
    record()
  })
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.locator('.three-card-poker-player .ff-playing-card--face')).toHaveCount(2)
  const trace = await page.evaluate(() => (window as Window & { houseDealTrace?: string[] }).houseDealTrace ?? [])
  let previous = -1
  for (const state of ['1:0', '2:0', '2:1', '2:2']) {
    const index = trace.indexOf(state)
    expect(index, `Missing ${state} in deal trace: ${trace.join(', ')}`).toBeGreaterThan(previous)
    previous = index
  }
  expect(trace.some((state) => Number(state.split(':')[1]) > 0 && Number(state.split(':')[0]) < 2)).toBe(false)
})

test('House Hold’em keeps a mandatory Play reserve and shows its exact payout guide on a narrow screen', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 667 })
  await mockHouseHoldem(page, 4)
  await page.goto('/cards/house-holdem')
  await page.getByRole('button', { name: 'Place chip on Ante' }).click()
  await page.getByRole('button', { name: 'Place chip on Trips' }).click()
  await expect(page.getByRole('button', { name: 'Place chip on Ante' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Place chip on Trips' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Wager 3 times your Ante' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Wager 4 times your Ante' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Check' })).toBeEnabled()
  await page.getByRole('button', { name: 'How to play House Hold’em' }).click()
  const guide = page.getByRole('dialog', { name: 'How to play House Hold’em' })
  await expect(guide).toContainText('Blind payouts')
  await expect(guide.getByRole('region', { name: 'Blind payouts' })).toContainText('500:1')
  await expect(guide).toContainText('Trips payouts')
  await expect(guide.getByRole('region', { name: 'Trips payouts' })).toContainText('3:1')
  await page.screenshot({ path: '../artifacts/house-holdem-ui/small-phone-guide.png' })
  await expectNoOverflow(page, 320, 667)
})

for (const viewport of [
  { width: 430, height: 932 },
  { width: 375, height: 667 },
  { width: 667, height: 375 },
] as const) {
  test(`House Hold’em guide fits a ${viewport.width}×${viewport.height} screen`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await mockHouseHoldem(page)
    await page.goto('/cards/house-holdem')
    await page.getByRole('button', { name: 'How to play House Hold’em' }).click()
    const guide = page.getByRole('dialog', { name: 'How to play House Hold’em' })
    const bounds = await guide.boundingBox()
    expect(bounds).not.toBeNull()
    expect(bounds!.x).toBeGreaterThanOrEqual(0)
    expect(bounds!.y).toBeGreaterThanOrEqual(0)
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width)
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height)
    await guide.evaluate((element) => { element.scrollTop = element.scrollHeight })
    await expect(guide.getByRole('region', { name: 'Trips payouts' })).toBeInViewport()
    await guide.getByRole('button', { name: 'Close how to play' }).click()
    await expect(guide).toHaveCount(0)
  })
}

test('House Hold’em explains every betting spot on hover and on small-screen focus', async ({ page }) => {
  await mockHouseHoldem(page)
  await page.goto('/cards/house-holdem')
  const tips = [
    { kind: 'trips', label: 'Trips', text: 'Optional side bet' },
    { kind: 'ante', label: 'Ante', text: 'required opening bet' },
    { kind: 'blind', label: 'Blind', text: 'matches your Ante' },
    { kind: 'play', label: 'Wager', text: 'Play bet' },
  ] as const
  for (const tip of tips) {
    await page.getByRole('button', { name: `About ${tip.label}` }).hover()
    await expect(page.getByRole('tooltip').filter({ hasText: tip.text })).toBeVisible()
  }
  await page.setViewportSize({ width: 320, height: 667 })
  for (const tip of tips) {
    await page.getByRole('button', { name: `About ${tip.label}` }).focus()
    const tooltip = page.getByRole('tooltip').filter({ hasText: tip.text })
    await expect(tooltip).toBeVisible()
    await expect(tooltip).toBeInViewport()
  }
  await page.screenshot({ path: '../artifacts/house-holdem-ui/small-phone-wager-help.png' })
})

async function expectNoOverflow(page: Page, width: number, height: number) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(height)
}

async function expectNoCardOverlap(page: Page, landscape: boolean) {
  for (const selector of ['.blackjack-dealer', '.house-holdem-board', '.three-card-poker-player']) {
    const boxes = await page.locator(`${selector} .blackjack-hand__cards > *`).evaluateAll((elements) => elements.map((element) => {
      const bounds = element.getBoundingClientRect()
      return { left: bounds.left, right: bounds.right }
    }))
    for (let index = 1; index < boxes.length; index++) expect(boxes[index].left - boxes[index - 1].right).toBeGreaterThanOrEqual(2)
  }
  if (landscape) return
  const dealer = await page.locator('.blackjack-dealer .blackjack-hand__cards').boundingBox()
  const community = await page.locator('.house-holdem-board .blackjack-hand__cards').boundingBox()
  const felt = await page.locator('.three-card-poker-felt strong').boundingBox()
  const player = await page.locator('.three-card-poker-player .blackjack-hand__cards').boundingBox()
  const bets = await page.locator('.house-holdem-bet-spot-wrap .three-card-poker-bet-spot__label').first().boundingBox()
  expect(dealer).not.toBeNull()
  expect(community).not.toBeNull()
  expect(felt).not.toBeNull()
  expect(player).not.toBeNull()
  expect(dealer!.y + dealer!.height).toBeLessThan(community!.y)
  expect(community!.y + community!.height).toBeLessThan(felt!.y)
  expect(felt!.y + felt!.height).toBeLessThan(player!.y)
  if (bets) expect(player!.y + player!.height).toBeLessThan(bets.y)
}

async function mockHouseHoldem(page: Page, initialBalance = 1000) {
  const fixture = { start: [] as unknown[], actions: [] as string[], advances: 0 }
  const base = '/api/games/house-holdem'
  let balance = initialBalance
  let round: Record<string, unknown> | null = null
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    const send = (json: unknown) => route.fulfill({ json })
    if (path === '/api/accounts/me') return send({ userId: 'house-player', playerName: 'Player', email: 'player@example.test', createdAtUtc: new Date().toISOString(), role: 'Player', balances: { slotsCredits: balance, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } })
    if (path === `${base}/status`) return send({ available: true, balance, activeRoundId: round && round.phase !== 'settled' ? round.roundId : null, minimumAnte: 1, maximumAnte: 100, wagerIncrement: 1, maximumTrips: 500 })
    if (path === `${base}/rounds` && method === 'POST') {
      const body = route.request().postDataJSON() as { ante: number; trips: number }
      fixture.start.push(body)
      balance -= body.ante * 2 + body.trips
      round = { roundId: 'round-one', balance, phase: 'preflop', ante: body.ante, blind: body.ante, trips: body.trips, play: 0,
        totalWager: body.ante * 2 + body.trips, payout: 0, playerCards: [card('ace', 'spades'), card('king', 'hearts')], dealerCards: null,
        board: [], playerHand: null, dealerHand: null, result: null, allowedActions: ['check', 'play-3x', 'play-4x'], canAdvance: false, payoutBreakdown: null }
      return send(round)
    }
    if (path === `${base}/rounds/round-one` && method === 'GET') return send(round)
    if (path.endsWith('/actions') && method === 'POST' && round) {
      const action = (route.request().postDataJSON() as { action: string }).action
      fixture.actions.push(action)
      balance -= 4
      round = { ...round, balance, phase: 'flop', play: 4, totalWager: 6, board: board.slice(0, 3), allowedActions: [], canAdvance: true }
      return send(round)
    }
    if (path.endsWith('/advance') && method === 'POST' && round) {
      fixture.advances++
      round = fixture.advances === 1
        ? { ...round, phase: 'river', board, canAdvance: true }
        : { ...round, phase: 'settled', board, dealerCards: [card('queen', 'clubs'), card('jack', 'diamonds')], playerHand: 'straight', dealerHand: 'pair', result: 'player-win', allowedActions: [], canAdvance: false, payout: 12, payoutBreakdown: { ante: 2, blind: 0, trips: 0, play: 10 }, balance: balance + 12 }
      if (fixture.advances === 2) balance += 12
      return send(round)
    }
    return send({})
  })
  return fixture
}

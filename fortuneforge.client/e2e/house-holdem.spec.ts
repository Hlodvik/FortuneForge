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
    const navbar = page.locator('[data-site-navbar]')
    await expect(navbar.getByRole('button', { name: 'How to play House Hold’em' })).toHaveCount(0)
    await expect(navbar.getByRole('button', { name: 'Report this game' })).toHaveCount(0)
    const support = page.getByRole('group', { name: 'Game help and reporting' })
    await expect(support.getByRole('button', { name: 'How to play House Hold’em' })).toBeInViewport()
    await expect(support.getByRole('button', { name: 'Report this game' })).toBeInViewport()
    await support.getByRole('button', { name: 'Report this game' }).click()
    await expect(page.getByRole('dialog', { name: 'Report House Hold’em' })).toBeVisible()
    await page.getByRole('button', { name: 'Close report' }).click()
    await expect(page.getByRole('button', { name: 'Place chip on Ante' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Place chip on Blind' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Place chip on Trips' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeDisabled()
    await page.getByRole('button', { name: `Place chip on ${viewport.name === 'desktop' ? 'Ante' : 'Blind'}` }).click()
    await expect(page.locator('.house-holdem-bet-spot--ante .three-card-poker-bet-spot__amount')).toHaveText('R1.00')
    await expect(page.locator('.house-holdem-bet-spot--blind .three-card-poker-bet-spot__amount')).toHaveText('R1.00')
    await expect(page.locator('.house-holdem-bet-spot--trips .three-card-poker-bet-spot__amount')).toHaveText('R0.00')
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

test('House Hold’em keeps cards, bets and controls separate while resizing throughout a hand', async ({ page }) => {
  test.setTimeout(60_000)
  const viewports = [
    { width: 1280, height: 640 },
    { width: 1024, height: 600 },
    { width: 800, height: 600 },
    { width: 768, height: 1024 },
    { width: 320, height: 480 },
    { width: 375, height: 568 },
    { width: 430, height: 932 },
    { width: 667, height: 375 },
    { width: 844, height: 390 },
    { width: 1920, height: 960 },
  ]
  await mockHouseHoldem(page, 138_506.46)
  await page.goto('/cards/house-holdem')
  await expect(page.getByRole('button', { name: 'Place chip on Ante', exact: true })).toBeVisible()
  await page.evaluate(() => document.fonts.ready)

  const checkResizing = async (phase: string) => {
    for (const viewport of viewports) {
      await test.step(`${phase} at ${viewport.width}×${viewport.height}`, async () => {
        await page.setViewportSize(viewport)
        await page.locator('.blackjack-table').evaluate(async (table) => {
          const finiteAnimations = table.getAnimations({ subtree: true }).filter((animation) =>
            animation.effect?.getTiming().iterations !== Infinity)
          await Promise.all(finiteAnimations.map((animation) => animation.finished.catch(() => undefined)))
        })
        await expectSeparatedTableContent(page)
      })
    }
  }

  await checkResizing('empty table')
  await page.getByRole('button', { name: 'Place chip on Blind', exact: true }).click()
  await checkResizing('betting')
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Wager 4 times your Ante', exact: true })).toBeVisible()
  await checkResizing('preflop')
  await page.getByRole('button', { name: 'Wager 4 times your Ante', exact: true }).click()
  await expect(page.getByRole('status', { name: 'Round result', exact: true })).toBeVisible()
  await checkResizing('settled with hand labels')
})

test('House Hold’em keeps placed chip denominations throughout the deal', async ({ page }) => {
  const fixture = await mockHouseHoldem(page)
  await page.goto('/cards/house-holdem')
  await page.getByRole('button', { name: 'Choose chip value, currently R1.00' }).click()
  await page.getByRole('button', { name: 'Select R5.00 chip' }).click()
  for (let index = 0; index < 4; index++) await page.getByRole('button', { name: 'Place chip on Ante' }).click()
  for (let index = 0; index < 2; index++) await page.getByRole('button', { name: 'Place chip on Trips' }).click()
  const chipStacks = () => page.locator('.house-holdem-bet-spot .three-card-poker-chip').evaluateAll(chips => chips.map(chip => chip.getAttribute('src')))
  const placed = await chipStacks()
  expect(placed).toHaveLength(10)
  expect(new Set(placed).size).toBe(1)
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await expect(page.locator('.blackjack-table')).toHaveAttribute('data-phase', 'dealing')
  expect(await chipStacks()).toEqual(placed)
  await expect(page.getByRole('button', { name: 'Wager 4 times your Ante' })).toBeVisible()
  expect(await chipStacks()).toEqual(placed)
  expect(fixture.start).toEqual([{ ante: 20, trips: 10 }])
})

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

async function expectSeparatedTableContent(page: Page) {
  const geometry = await page.evaluate(() => {
    const selectors = [
      '.three-card-poker-limits',
      '.blackjack-dealer .blackjack-hand__heading',
      '.blackjack-dealer .blackjack-hand__cards > *',
      '.house-holdem-board .blackjack-hand__cards > *',
      '.house-holdem-felt',
      '.three-card-poker-player .blackjack-hand__heading',
      '.three-card-poker-player .blackjack-hand__cards > *',
      '.house-holdem-bet-spot__help',
      '.house-holdem-bet-spot .three-card-poker-bet-spot__amount',
      '.house-holdem-bet-spot .three-card-poker-chip-stack:has(img)',
      '.house-holdem-opening-bet-hint',
      '.blackjack-actions .three-card-poker-action-orb',
      '.blackjack-actions .three-card-poker-chip-picker__selected',
      '.three-card-poker-action-stats > span',
      '.game-support-controls > button, .game-support-controls > div > button',
    ]
    const rect = (element: Element) => {
      const { left, right, top, bottom, width, height } = element.getBoundingClientRect()
      return { left, right, top, bottom, width, height }
    }
    const boxes = selectors.flatMap((selector) => Array.from(document.querySelectorAll(selector), (element, index) => ({
      name: `${selector} [${index}]`,
      viewportBound: element.closest('.game-support-controls') !== null,
      ...rect(element),
    }))).filter((box) => box.width > 0 && box.height > 0)
    const controls = Array.from(document.querySelectorAll('.blackjack-actions .three-card-poker-action-orb, .blackjack-actions .three-card-poker-chip-picker__selected'), rect)
    return { boxes, controls, table: rect(document.querySelector('.blackjack-table')!), width: innerWidth, height: innerHeight, scrollWidth: document.documentElement.scrollWidth }
  })

  expect(geometry.scrollWidth, 'The table must not require horizontal scrolling').toBeLessThanOrEqual(geometry.width)
  for (const box of geometry.boxes) {
    const boundary = box.viewportBound ? { left: 0, right: geometry.width, top: 0, bottom: geometry.height } : geometry.table
    expect(box.left, `${box.name} clips its left boundary`).toBeGreaterThanOrEqual(boundary.left - 1)
    expect(box.right, `${box.name} clips its right boundary`).toBeLessThanOrEqual(boundary.right + 1)
    expect(box.top, `${box.name} clips its top boundary`).toBeGreaterThanOrEqual(boundary.top - 1)
    expect(box.bottom, `${box.name} clips its bottom boundary`).toBeLessThanOrEqual(boundary.bottom + 1)
  }
  for (let index = 0; index < geometry.boxes.length; index++) {
    const a = geometry.boxes[index]
    for (const b of geometry.boxes.slice(index + 1)) {
      const overlapWidth = Math.min(a.right, b.right) - Math.max(a.left, b.left)
      const overlapHeight = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top)
      expect(overlapWidth > 1 && overlapHeight > 1, `${a.name} overlaps ${b.name}`).toBe(false)
    }
  }
  expect(geometry.controls).toHaveLength(3)
  for (const control of geometry.controls) {
    expect(control.width, 'Chip and action controls must share a diameter').toBeCloseTo(geometry.controls[0].width, 0)
    expect(control.height, 'Controls must remain circular').toBeCloseTo(control.width, 0)
  }
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

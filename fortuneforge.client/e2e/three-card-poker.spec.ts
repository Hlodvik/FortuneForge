import { expect, test, type Page } from '@playwright/test'
import type { ThreeCardPokerPlaySession, ThreeCardPokerSession } from '../src/games/cards/threeCardPoker/threeCardPokerApi'

for (const viewport of [
  { name: 'desktop', width: 1280, height: 720, action: 'play' },
  { name: 'mobile', width: 390, height: 844, action: 'fold' },
  { name: 'small-phone', width: 320, height: 667, action: 'fold' },
  { name: 'landscape', width: 844, height: 390, action: 'play' },
] as const) {
  test(`Three Card Poker plays a solo hand and ${viewport.action}s in a ${viewport.name} viewport`, async ({ page }) => {
    await page.setViewportSize(viewport)
    const fixture = await mockTable(page)
    await page.goto('/cards/three-card-poker')
    await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Play', exact: true }).click()
    const anteSpot = page.getByRole('button', { name: 'Place chip on Ante' })
    const pairPlusSpot = page.getByRole('button', { name: 'Place chip on Pair Plus' })
    await expect(anteSpot).toBeVisible()
    await expect(pairPlusSpot).toBeVisible()
    await expect(page.locator('.three-card-poker-player')).toHaveCount(1)
    await expect(page.locator('.three-card-poker-player')).not.toContainText('Ada')
    await expect(page.getByText(/Finding table|Open seat|Mina|Leo/i)).toHaveCount(0)
    await expect(page.getByLabel('Wager spot')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Choose chip value, currently R1.00' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Select R2.00 chip' })).toHaveCount(0)
    const bet = page.locator('.three-card-poker-action-stats span').first().locator('strong')
    await expect(bet).toHaveText('R0.00')
    if (viewport.name === 'desktop' || viewport.name === 'small-phone') await expectActionStatsPositions(page)
    await page.getByRole('button', { name: 'Choose chip value, currently R1.00' }).click()
    await page.getByRole('button', { name: 'Select R2.00 chip' }).click()
    await expect(page.locator('.three-card-poker-chip-picker__options')).toHaveCount(0)
    await expect(bet).toHaveText('R0.00')
    await anteSpot.click()
    await expect(bet).toHaveText('R2.00')
    await anteSpot.click()
    await expect(bet).toHaveText('R4.00')
    await page.getByRole('button', { name: 'Choose chip value, currently R2.00' }).click()
    await page.getByRole('button', { name: 'Select R1.00 chip' }).click()
    await expect(bet).toHaveText('R4.00')
    await anteSpot.click()
    await expect(bet).toHaveText('R5.00')
    await pairPlusSpot.click()
    await expect(bet).toHaveText('R6.00')
    await expect(anteSpot).toContainText('R5.00')
    await expect(pairPlusSpot).toContainText('R1.00')
    await expect(page.locator('.three-card-poker-bet-spot--ante .three-card-poker-chip')).toHaveCount(3)
    await expect(page.locator('.three-card-poker-bet-spot--pair-plus .three-card-poker-chip')).toHaveCount(1)
    const dealButton = page.getByRole('button', { name: 'Deal', exact: true })
    await expect(dealButton).toBeInViewport()
    await expect(page.getByRole('button', { name: 'Leave table' })).toBeInViewport()
    await expect(page.getByText(/\d+s remaining|Next round \d|Wager timer/i)).toHaveCount(0)
    await expectCardSpacing(page, viewport.name !== 'landscape')
    await page.screenshot({ path: `../artifacts/three-card-poker-ui/${viewport.name}-betting.png` })
    await dealButton.click()
    await expect(bet).toHaveText('R6.00')
    const wagerSpot = page.getByRole('button', { name: /^Add R1\.00 chip to Wager/ })
    await expect(wagerSpot).toBeEnabled()
    await expect(wagerSpot).toBeInViewport()
    await expect(wagerSpot).toHaveClass(/three-card-poker-bet-spot--play/)
    await expect(page.getByRole('button', { name: 'Play R5.00' })).toBeDisabled()
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(3)
    await expect(page.locator('.ff-playing-card--back')).toHaveCount(3)
    await expectCardSpacing(page, viewport.name !== 'landscape')
    await page.screenshot({ path: `../artifacts/three-card-poker-ui/${viewport.name}-active.png` })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width)
    expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(viewport.height)
    if (viewport.action === 'play') {
      await wagerSpot.click()
      await page.getByRole('button', { name: 'Choose chip value, currently R1.00' }).click()
      await page.getByRole('button', { name: 'Select R2.00 chip' }).click()
      const twoChipWager = page.getByRole('button', { name: /^Add R2\.00 chip to Wager/ })
      await twoChipWager.click()
      await twoChipWager.click()
      await expect(page.locator('.three-card-poker-bet-spot--play .three-card-poker-chip')).toHaveCount(3)
      await expect(bet).toHaveText('R11.00')
      await page.getByRole('button', { name: 'Undo wager chip' }).click()
      await expect(bet).toHaveText('R9.00')
      await twoChipWager.click()
      await page.getByRole('button', { name: 'Play R5.00' }).click()
    } else {
      await page.getByRole('button', { name: 'Fold', exact: true }).click()
    }
    await expect(page.locator('.blackjack-table')).toHaveAttribute('data-phase', 'settled')
    await expect(page.locator('.ff-playing-card--face')).toHaveCount(6)
    await expect(bet).toHaveText(viewport.action === 'play' ? 'R11.00' : 'R6.00')
    const result = page.getByRole('status', { name: 'Round result' })
    await expect(result).toBeVisible()
    await expect(result).toContainText(viewport.action === 'play' ? 'Win' : 'Loss')
    await expect(result).toContainText(viewport.action === 'play' ? '+R9.00' : '−R6.00')
    await expect(page.locator('.three-card-poker-action-stats span').last()).toContainText(viewport.action === 'play' ? 'R9.00' : 'R0.00')
    await page.screenshot({ path: `../artifacts/three-card-poker-ui/${viewport.name}-settled.png` })
    await page.getByRole('button', { name: 'Close round result' }).click()
    await expect(result).toHaveCount(0)
    expect(fixture.wagers).toEqual([{ ante: 5, pairPlus: 1, expectedVersion: 2 }])
    expect(fixture.actions).toEqual([{ type: viewport.action, expectedVersion: 3 }])
    await page.getByRole('button', { name: 'How to play Three Card Poker' }).click()
    await expect(page.getByRole('dialog')).toContainText('It remains eligible when you fold.')
    await page.getByRole('button', { name: 'Close how to play' }).click()
    await page.getByRole('button', { name: 'Leave table' }).click()
    await expect(page.getByRole('button', { name: 'Play', exact: true })).toBeVisible()
  })
}

async function expectCardSpacing(page: Page, stackedLayout: boolean) {
  const cards = await page.locator('.blackjack-dealer .blackjack-hand__cards > *').evaluateAll((elements) => elements.map((element) => {
    const bounds = element.getBoundingClientRect()
    return { left: bounds.left, right: bounds.right }
  }))
  expect(cards).toHaveLength(3)
  expect(cards[1].left - cards[0].right).toBeGreaterThan(5)
  expect(cards[2].left - cards[1].right).toBeGreaterThan(5)
  if (!stackedLayout) return
  const dealerCards = await page.locator('.blackjack-dealer .blackjack-hand__cards').boundingBox()
  const feltTitle = await page.locator('.three-card-poker-felt strong').boundingBox()
  const playerCards = await page.locator('.three-card-poker-player .blackjack-hand__cards').boundingBox()
  const betLabel = await page.locator('.three-card-poker-bet-spot__label').first().boundingBox()
  expect(dealerCards).not.toBeNull()
  expect(feltTitle).not.toBeNull()
  expect(playerCards).not.toBeNull()
  expect(betLabel).not.toBeNull()
  expect(dealerCards!.y + dealerCards!.height).toBeLessThan(feltTitle!.y)
  expect(playerCards!.y + playerCards!.height).toBeLessThan(betLabel!.y)
}

async function expectActionStatsPositions(page: Page) {
  const actions = page.locator('.blackjack-actions')
  const actionBounds = await actions.boundingBox()
  expect(actionBounds).not.toBeNull()
  const centers = await page.locator('.three-card-poker-action-stats span').evaluateAll((elements) => elements.map((element) => {
    const bounds = element.getBoundingClientRect()
    return bounds.left + bounds.width / 2
  }))
  expect(centers).toHaveLength(3)
  for (const [index, target] of [0.3, 0.5, 0.7].entries()) {
    expect(Math.abs((centers[index] - actionBounds!.x) / actionBounds!.width - target)).toBeLessThan(0.02)
  }
}

test('Three Card Poker chip picker stays one chip wide until expanded', async ({ page }) => {
  await mockTable(page)
  await page.goto('/cards/three-card-poker')
  await page.getByRole('button', { name: 'Play', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Place chip on Ante' })).toBeVisible()
  const picker = page.getByRole('button', { name: 'Choose chip value, currently R1.00' })
  await expect(picker).toHaveAttribute('aria-expanded', 'false')
  await expect(page.locator('.three-card-poker-chip-picker__options')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Decrease chip value' })).toBeDisabled()
  await page.getByRole('button', { name: 'Increase chip value' }).click()
  await expect(page.getByRole('button', { name: 'Choose chip value, currently R2.00' })).toBeVisible()
  await page.getByRole('button', { name: 'Decrease chip value' }).click()
  await expect(picker).toBeVisible()
  await picker.click()
  await expect(page.locator('.three-card-poker-chip-picker__selected')).toHaveAttribute('aria-expanded', 'true')
  for (const value of [1, 2, 5, 10, 25]) {
    await expect(page.getByRole('button', { name: `Select R${value.toFixed(2)} chip` })).toBeVisible()
  }
  await expect(page.locator('.three-card-poker-chip-picker__options button')).toHaveCount(5)
  await expect.poll(() => page.locator('.three-card-poker-chip-picker__options img').evaluateAll((images) => images.map((image) => {
    const chip = image as HTMLImageElement
    return [chip.naturalWidth, chip.naturalHeight]
  }))).toEqual(Array.from({ length: 5 }, () => [64, 64]))
  await page.screenshot({ path: '../artifacts/three-card-poker-ui/desktop-chip-picker.png' })
  await page.setViewportSize({ width: 320, height: 667 })
  await expect(page.getByRole('button', { name: 'Select R25.00 chip' })).toBeInViewport()
  await page.screenshot({ path: '../artifacts/three-card-poker-ui/small-phone-chip-picker.png' })
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320)
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.getByRole('button', { name: 'Select R25.00 chip' }).click()
  await expect(page.getByRole('button', { name: 'Choose chip value, currently R25.00' })).toHaveAttribute('aria-expanded', 'false')
  await expect(page.getByRole('button', { name: 'Increase chip value' })).toBeDisabled()
  await page.getByRole('button', { name: 'Decrease chip value' }).click()
  await expect(page.getByRole('button', { name: 'Choose chip value, currently R10.00' })).toBeVisible()
  await page.getByRole('button', { name: 'Increase chip value' }).click()
  await expect(page.getByRole('button', { name: 'Choose chip value, currently R25.00' })).toBeVisible()
  await expect(page.locator('.three-card-poker-chip-picker__options')).toHaveCount(0)
  await expect(page.getByText('MAX BET R500.00')).toBeVisible()
  const anteSpot = page.getByRole('button', { name: 'Place chip on Ante' })
  for (let index = 0; index < 20; index++) await anteSpot.click()
  await expect(anteSpot).toContainText('R500.00')
  await expect(anteSpot).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Place chip on Pair Plus' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Deal', exact: true })).toBeEnabled()
  await page.screenshot({ path: '../artifacts/three-card-poker-ui/desktop-cap.png' })
})

test('Three Card Poker piles chips vertically and undo removes only the last placement', async ({ page }) => {
  const fixture = await mockTable(page)
  await page.goto('/cards/three-card-poker')
  await page.getByRole('button', { name: 'Play', exact: true }).click()
  const anteSpot = page.getByRole('button', { name: 'Place chip on Ante' })
  const pairPlusSpot = page.getByRole('button', { name: 'Place chip on Pair Plus' })
  await expect(anteSpot).toBeVisible()
  await page.getByRole('button', { name: 'Choose chip value, currently R1.00' }).click()
  await page.getByRole('button', { name: 'Select R2.00 chip' }).click()
  await anteSpot.click({ clickCount: 2 })
  await page.getByRole('button', { name: 'Choose chip value, currently R2.00' }).click()
  await page.getByRole('button', { name: 'Select R1.00 chip' }).click()
  await anteSpot.click()
  await expect(anteSpot).toContainText('R5.00')
  const stack = page.locator('.three-card-poker-bet-spot--ante .three-card-poker-chip')
  await expect(stack).toHaveCount(3)
  const chips = await stack.evaluateAll((elements) => elements.map((element) => {
    const rect = element.getBoundingClientRect()
    return { x: rect.x, y: rect.y }
  }))
  expect(Math.max(...chips.map((chip) => chip.x)) - Math.min(...chips.map((chip) => chip.x))).toBeLessThan(3)
  expect(Math.max(...chips.map((chip) => chip.y)) - Math.min(...chips.map((chip) => chip.y))).toBeGreaterThan(2)
  await page.screenshot({ path: '../artifacts/three-card-poker-ui/desktop-chip-stack.png' })
  await pairPlusSpot.click()
  await expect(pairPlusSpot).toContainText('R1.00')
  await page.getByRole('button', { name: 'Undo chip' }).click()
  await expect(pairPlusSpot).toContainText('R0.00')
  await expect(anteSpot).toContainText('R5.00')
  await pairPlusSpot.click()
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  expect(fixture.wagers).toEqual([{ ante: 5, pairPlus: 1, expectedVersion: 2 }])
})

test('Three Card Poker keeps a result available until the player dismisses it', async ({ page }) => {
  const fixture = await mockTable(page)
  await page.goto('/cards/three-card-poker')
  await page.getByRole('button', { name: 'Play', exact: true }).click()
  await page.getByRole('button', { name: 'Place chip on Ante' }).click()
  await page.getByRole('button', { name: 'Deal', exact: true }).click()
  await page.getByRole('button', { name: 'Fold', exact: true }).click()
  const result = page.getByRole('status', { name: 'Round result' })
  await expect(result).toBeVisible()
  fixture.advanceToNextRound()
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect(page.locator('.blackjack-table')).toHaveAttribute('data-phase', 'betting')
  await expect(result).toBeVisible()
  await page.getByRole('button', { name: 'Dismiss round result' }).click({ position: { x: 5, y: 5 } })
  await expect(result).toHaveCount(0)
  fixture.settleNextRound()
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect(result).toBeVisible()
  await expect(result).toContainText('−R1.00')
})

async function mockTable(page: Page) {
  const date = new Date().toISOString()
  const deadline = new Date(Date.now() + 60_000).toISOString()
  const base = '/api/cards/three-card-poker/table'
  const fixture = { wagers: [] as unknown[], actions: [] as unknown[] }
  let balance = 1000
  let session: ThreeCardPokerSession = { contractVersion: 'cards.three-card-poker.table.v1', kind: 'idle', version: 0 }
  const table: ThreeCardPokerPlaySession = {
    contractVersion: 'cards.three-card-poker.table.v1', kind: 'table', version: 2,
    table: {
      tableId: 'table-1', phase: 'betting', round: 1, dealer: { cards: [], rank: null },
      seats: [{ seatId: 'seat-0', displayName: 'Ada', seat: 0, status: 'awaiting-wager', ante: 0, pairPlus: 0, playWager: 0, totalWager: 0, payout: 0, anteBonusPayout: 0, pairPlusPayout: 0, hand: { cards: [], rank: null }, isCurrentPlayer: true }],
      activeSeat: 0, legalActions: [], createdAtUtc: date, updatedAtUtc: date, actionDeadlineAtUtc: null,
      wagerDeadlineAtUtc: deadline, transition: 'human-wager', nextTransitionAtUtc: null,
      remainingActionMilliseconds: 0, remainingWagerMilliseconds: 60_000, remainingTransitionMilliseconds: 0,
    },
  }
  const cards = [{ hidden: false, rank: 'Q', suit: 'spades' }, { hidden: false, rank: '6', suit: 'hearts' }, { hidden: false, rank: '4', suit: 'diamonds' }] as const
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    const send = (json: unknown) => route.fulfill({ json })
    if (path === '/api/accounts/me') return send({ userId: 'table-player', playerName: 'Ada', email: 'ada@example.test', createdAtUtc: date, role: 'Player', balances: { slotsCredits: balance, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } })
    if (path === `${base}/status`) return send({ available: true, minimumWager: 1, maximumWager: 500, wagerIncrement: 1, minimumStartOccupancy: 1, tableCapacity: 1, humanGraceSeconds: 0, actionDeadlineSeconds: 60, dealerRule: 'Queen high or better qualifies', pairPlusAllowed: true, anteBonusPaytable: 'Straight 1:1; trips 4:1; straight flush 5:1', pairPlusPaytable: 'Pair 1:1; flush 3:1; straight 6:1; trips 30:1; straight flush 40:1', pairPlusPaysOnFold: true })
    if (path === `${base}/queue` && method === 'POST') {
      session = table
    } else if (path.endsWith('/wagers')) {
      const request = route.request().postDataJSON() as { ante: number; pairPlus: number }
      fixture.wagers.push(request)
      balance -= request.ante + request.pairPlus
      session = { ...table, version: 3, table: { ...table.table, phase: 'active', transition: null, actionDeadlineAtUtc: deadline, wagerDeadlineAtUtc: null, legalActions: ['play', 'fold'], dealer: { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null }, seats: table.table.seats.map((seat) => ({ ...seat, status: 'playing', ante: 5, pairPlus: seat.isCurrentPlayer ? 1 : 0, totalWager: seat.isCurrentPlayer ? 6 : 5, hand: seat.isCurrentPlayer ? { cards, rank: 'high-card' } : { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null } })) } }
    } else if (path.endsWith('/actions') && session.kind === 'table') {
      const request = route.request().postDataJSON() as { type: string }
      fixture.actions.push(request)
      if (request.type === 'play') balance += 15
      session = { ...session, version: 4, table: { ...session.table, phase: 'settled', transition: null, activeSeat: null, legalActions: [], dealer: { cards: [{ hidden: false, rank: 'Q', suit: 'clubs' }, { hidden: false, rank: '5', suit: 'hearts' }, { hidden: false, rank: '3', suit: 'diamonds' }], rank: 'high-card' }, seats: session.table.seats.map((seat) => ({ ...seat, hand: { cards, rank: 'high-card' }, status: 'completed', outcome: request.type === 'play' ? 'player-win' : 'fold', lastAction: request.type, playWager: request.type === 'play' ? 5 : 0, totalWager: request.type === 'play' ? seat.totalWager + 5 : seat.totalWager, payout: seat.isCurrentPlayer && request.type === 'play' ? 20 : 0 })) } }
    } else if (path.endsWith('/leave')) {
      session = { contractVersion: 'cards.three-card-poker.table.v1', kind: 'idle', version: 0 }
    }
    if (path.startsWith(base)) return send({ session, balanceCredits: balance })
    if (path === '/api/cards/history') return send([])
    if (path.endsWith('/session')) return send({ kind: 'idle' })
    return send({})
  })
  return {
    ...fixture,
    advanceToNextRound: () => {
      if (session.kind !== 'table') throw new Error('No active table to advance')
      session = { ...table, version: session.version + 1, table: { ...table.table, round: session.table.round + 1 } }
    },
    settleNextRound: () => {
      if (session.kind !== 'table') throw new Error('No active table to settle')
      session = { ...session, version: session.version + 1, table: {
        ...session.table, phase: 'settled', transition: null, activeSeat: null, legalActions: [],
        seats: session.table.seats.map((seat) => ({ ...seat, status: 'completed', ante: 1, totalWager: 1, payout: 0, outcome: 'fold', lastAction: 'fold' })),
      } }
    },
  }
}

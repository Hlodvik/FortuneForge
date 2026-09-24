import { expect, test, type Page } from '@playwright/test'

const previews = [
  { name: 'Blackjack', path: '/src/pages/cards/blackjack/preview.html', actions: '.blackjack-actions' },
  { name: 'Texas Hold’em', path: '/src/pages/cards/texasHoldem/preview.html', actions: '.holdem-controls' },
  { name: 'Solitaire', path: '/src/pages/cards/solitaire/preview.html', actions: '.solitaire-match__controls' },
] as const

for (const preview of previews) {
  test(`${preview.name} keeps the playable surface in a desktop viewport`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 })
    await openPreview(page, preview.path)

    expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(720)
    await expect(page.locator(preview.actions)).toBeInViewport()
  })

  test(`${preview.name} keeps primary actions in the first phone viewport`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await openPreview(page, preview.path)

    const bottom = await page.locator(preview.actions).evaluate((element) =>
      element.getBoundingClientRect().bottom)
    expect(bottom).toBeLessThanOrEqual(844)
    if (preview.name === 'Texas Hold’em') await expect(page.locator('[data-game-navbar]')).toHaveCount(1)
  })
}

test('Blackjack preserves table geometry between betting and active play', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  const heights: number[] = []
  for (const mode of ['active', 'betting']) {
    await openPreview(page, `/src/pages/cards/blackjack/preview.html?mode=${mode}`)
    heights.push(await page.locator('.blackjack-playfield').evaluate((element) =>
      element.getBoundingClientRect().height))
  }

  expect(Math.abs(heights[0] - heights[1])).toBeLessThan(1)
  const opponent = page.locator('.blackjack-seat').filter({ hasText: 'Mina' })
  expect(await opponent.locator('.ff-card-slot').count()).toBe(6)
  const bounds = await opponent.evaluate((seat) => {
    const hand = seat.querySelector('.blackjack-hand__cards')!.getBoundingClientRect()
    const box = seat.getBoundingClientRect()
    return { handLeft: hand.left, handRight: hand.right, boxLeft: box.left, boxRight: box.right }
  })
  expect(bounds.handLeft).toBeGreaterThanOrEqual(bounds.boxLeft - 1)
  expect(bounds.handRight).toBeLessThanOrEqual(bounds.boxRight + 1)
})

test('Blackjack demo keeps its deal controls in a short phone viewport', async ({ page }) => {
  await page.route('**/api/cards/blackjack/demo/status', (route) => route.fulfill({ json: blackjackStatus }))
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto('/demo/cards/blackjack')
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(720)
  await expect(page.locator('.blackjack-controls')).toBeInViewport()

  await page.setViewportSize({ width: 320, height: 667 })
  await page.goto('/demo/cards/blackjack')
  await expect(page.locator('[data-game-navbar]')).toHaveCount(1)
  await expect(page.locator('.blackjack-header')).toHaveCount(0)
  await expect(page.locator('.blackjack-controls')).toBeVisible()
  expect(await page.locator('.blackjack-controls').evaluate((element) =>
    element.getBoundingClientRect().bottom)).toBeLessThanOrEqual(667)
})

test('Solitaire practice uses one host navbar and keeps setup playable', async ({ page }) => {
  await page.route('**/api/cards/solitaire/bot-practice/session', (route) => route.fulfill({ status: 404 }))

  for (const viewport of [{ width: 1280, height: 720 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport)
    await page.goto('/demo/cards/solitaire/bot-practice')
    await expect(page.getByRole('button', { name: 'Join table' })).toBeVisible()
    await expect(page.locator('[data-game-navbar]')).toHaveCount(1)
    await expect(page.locator('.practice-bot-header')).toHaveCount(0)
    expect(await page.getByRole('button', { name: 'Join table' }).evaluate((element) =>
      element.getBoundingClientRect().bottom)).toBeLessThanOrEqual(viewport.height)
    if (viewport.width > 720) {
      expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(viewport.height)
    }
  }
})

test('card surfaces suppress accidental selection without disabling Solitaire pointer drag', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await openPreview(page, '/src/pages/cards/solitaire/preview.html')

  const card = page.locator('.solitaire-card-button').first()
  await expect(card).toHaveAttribute('draggable', 'false')
  expect(await card.evaluate((element) => getComputedStyle(element).touchAction)).toBe('none')
  expect(await page.locator('.solitaire-page').evaluate((element) => getComputedStyle(element).userSelect)).toBe('none')
})

test('Texas Hold’em practice opens directly into a playable, automation-neutral table', async ({ page }) => {
  await page.route('**/api/cards/texas-holdem/bot-practice/session', (route) =>
    route.fulfill({ json: holdemPracticeMatch }))

  for (const viewport of [{ width: 1280, height: 720 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport)
    await page.goto('/demo/cards/texas-holdem/bot-practice')
    await expect(page.locator('[data-game-navbar]')).toHaveCount(1)
    await expect(page.locator('.practice-bot-header')).toHaveCount(0)
    await expect(page.locator('.practice-bot-actions')).toBeVisible()
    expect(await page.locator('.practice-bot-actions').evaluate((element) =>
      element.getBoundingClientRect().bottom)).toBeLessThanOrEqual(viewport.height)
    expect(await page.locator('body').innerText()).not.toMatch(/\bbots?\b/i)
  }
})

async function openPreview(page: Page, path: string) {
  await page.goto(path)
  await page.addStyleTag({ content: 'html, body { margin: 0 !important; }' })
}

const hiddenCard = { rank: null, suit: null, hidden: true } as const
const blackjackStatus = {
  available: true, minimumWager: .5, maximumWager: 100, wagerIncrement: .5,
  dealerRule: 'Dealer stands on all 17s', blackjackPayout: '3:2',
  doubleAllowed: true, splitAllowed: false, insuranceAllowed: false,
} as const
const holdemPracticeMatch = {
  contractVersion: 'cards.bot.v2',
  kind: 'match',
  queue: null,
  table: {
    matchId: 'viewport-match', status: 'active', street: 'preflop', version: 1,
    dealerSeat: 0, activeSeat: 0, pot: 30, currentBet: 20, minimumRaiseTo: 40,
    communityCards: [],
    seats: [
      practiceSeat(0, 'Player', [{ rank: 'A', suit: 'spades', hidden: false }, { rank: 'K', suit: 'spades', hidden: false }]),
      practiceSeat(1, 'Mina', [hiddenCard, hiddenCard]),
      practiceSeat(2, 'Leo', [hiddenCard, hiddenCard]),
      practiceSeat(3, 'Nova', [hiddenCard, hiddenCard]),
    ],
    events: [], legalActions: ['fold', 'call', 'raise'],
    startedAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
  },
} as const

function practiceSeat(seat: number, displayName: string, holeCards: readonly object[]) {
  return {
    player: { seatId: `seat-${seat}`, displayName, seat, status: 'playing' },
    holeCards, stack: 980, committed: seat === 0 ? 10 : 20,
    status: 'active', handName: null, payout: 0,
  }
}

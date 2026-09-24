import { expect, test, type Page } from '@playwright/test'

const account = {
  userId: 'casino-viewport-player',
  playerName: 'Table Tester',
  email: 'tables@example.test',
  createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 1000, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  role: 'Player',
}

const routes = [
  { name: 'Baccarat', path: '/games/baccarat', surface: '.ff-baccarat__table', controls: '.ff-baccarat__primary' },
  { name: 'Casino War', path: '/games/casino-war', surface: '.ff-casino-war__table', controls: '.ff-casino-war__primary' },
  { name: 'Keno', path: '/games/keno', surface: '.ff-keno__board', controls: '.ff-keno__play' },
  { name: 'Video Poker', path: '/games/video-poker', surface: '.ff-video-poker__console', controls: '.ff-video-poker__primary' },
  { name: 'Roulette', path: '/games/roulette', surface: '.ff-roulette-table', controls: '.ff-roulette-controls .primary' },
  { name: 'Craps', path: '/games/craps', surface: '.ff-craps-table', controls: '.ff-craps-controls' },
  { name: 'Sic Bo', path: '/games/sic-bo', surface: '.ff-sic-bo__layout', controls: '.ff-sic-bo__primary' },
  { name: 'Liar’s Dice', path: '/games/liars-dice', surface: '.ff-liars-table', controls: '.ff-liars-actions' },
] as const

for (const viewport of [
  { width: 1280, height: 720, desktop: true },
  { width: 390, height: 844, desktop: false },
] as const) {
  test(`casino tables fit ${viewport.width}x${viewport.height}`, async ({ page }) => {
    test.setTimeout(45_000)
    await page.setViewportSize(viewport)
    await mockCasinoApi(page)

    for (const route of routes) {
      await test.step(route.name, async () => {
        await page.goto(route.path)
        const navbar = page.locator('[data-game-navbar]')
        const surface = page.locator(route.surface)
        const controls = page.locator(route.controls).first()

        await expect(navbar).toBeVisible()
        await expect(navbar.getByRole('link', { name: 'Fortune Forge home' })).toHaveAttribute('href', '/home')
        await expect(navbar.getByRole('link', { name: 'Other Games' })).toHaveAttribute('href', '/games')
        await expect(page.locator('[data-game-navbar]')).toHaveCount(1)
        await expect(surface).toBeVisible()
        await expect(controls).toBeVisible()

        const geometry = await page.evaluate(({ surfaceSelector, controlsSelector }) => {
          const bottom = (selector: string) => document.querySelector(selector)?.getBoundingClientRect().bottom ?? null
          return {
            documentHeight: document.documentElement.scrollHeight,
            bodyHeight: document.body.scrollHeight,
            surfaceBottom: bottom(surfaceSelector),
            controlsBottom: bottom(controlsSelector),
          }
        }, { surfaceSelector: route.surface, controlsSelector: route.controls })

        expect(geometry.surfaceBottom, `${route.name} surface`).not.toBeNull()
        expect(geometry.controlsBottom, `${route.name} controls`).not.toBeNull()
        expect(geometry.surfaceBottom!, `${route.name} surface bottom`).toBeLessThanOrEqual(viewport.height + 1)
        expect(geometry.controlsBottom!, `${route.name} controls bottom`).toBeLessThanOrEqual(viewport.height + 1)
        if (viewport.desktop) {
          expect(geometry.documentHeight, `${route.name} document height`).toBeLessThanOrEqual(viewport.height + 1)
          expect(geometry.bodyHeight, `${route.name} body height`).toBeLessThanOrEqual(viewport.height + 1)
        }
      })
    }
  })
}

async function mockCasinoApi(page: Page): Promise<void> {
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    let body: unknown

    if (path === '/api/accounts/me') body = account
    else if (path === '/api/games/baccarat/status') body = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, balance: 1000, mode: 'test' }
    else if (path === '/api/games/casino-war/status') body = { available: true, minimumPrimaryStake: 1, maximumPrimaryStake: 100, stakeIncrement: 1, maximumTieStake: 25, balance: 1000, mode: 'test' }
    else if (path === '/api/games/keno/status') body = { available: true, balance: 1000, mode: 'test' }
    else if (path === '/api/games/video-poker/status') body = { available: true, minimumCoinsWagered: 1, maximumCoinsWagered: 5, coinValue: 1, balance: 1000, handCounts: [1, 3, 5] }
    else if (path === '/api/games/roulette/status') body = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, startingBalance: 1000, mode: 'test' }
    else if (path === '/api/games/craps/status') body = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, mode: 'test' }
    else if (path === '/api/games/sic-bo/status') body = { available: true, minimumStake: 1, maximumStakePerBet: 100, stakeIncrement: 1, maximumBetsPerRound: 8, balance: 1000, mode: 'test' }
    else if (path === '/api/games/liars-dice/status') body = { available: true, startingDicePerPlayer: 5, mode: 'test' }
    else if (path === '/api/games/liars-dice/matches') body = {
      matchId: 'viewport-match', phase: 'bidding', roundNumber: 1, currentPlayerId: 'you', currentBid: null,
      currentBidderId: null, totalDice: 20, hand: [1, 2, 3, 4, 5], outcome: null, winner: null,
      players: [
        { id: 'you', displayName: 'You', diceCount: 5, active: true, isHuman: true },
        { id: 'bot-1', displayName: 'Riley', diceCount: 5, active: true, isHuman: false },
        { id: 'bot-2', displayName: 'Morgan', diceCount: 5, active: true, isHuman: false },
        { id: 'bot-3', displayName: 'Casey', diceCount: 5, active: true, isHuman: false },
      ],
      message: 'Make the opening bid.',
    }
    else {
      await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'test-unavailable', message: 'Not needed for viewport testing.' }) })
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
}

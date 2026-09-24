import { expect, test, type Page } from '@playwright/test'

type ArcadeRoute = Readonly<{
  name: string
  path: string
  start?: string
  board: string
  controls: string
}>

const account = {
  userId: 'viewport-player',
  playerName: 'Viewport Tester',
  email: 'viewport@example.test',
  createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 1000, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  role: 'Player',
}

const game2048 = {
  gameId: '2048-test', size: 4, tiles: [2, 0, 0, 0, 0, 4, 0, 0, 0, 0, 2, 0, 0, 0, 0, 4],
  score: 12, moves: 3, highestTile: 4, phase: 'playing', canUndo: true,
  lastEvent: 'started', scoreGained: 0, message: 'Join matching tiles.',
}

const dropMerge = {
  gameId: 'drop-test', columns: 7, rows: 7, tiles: Array(49).fill(0), currentTile: 2, nextTile: 4,
  score: 0, moves: 0, combo: 0, bestCombo: 0, totalMerges: 0, smallestTileClearCount: 0,
  nextBigTile: 1024, highestTile: 2, emptyTileCount: 49, phase: 'playing', canUndo: false,
  lastEvent: 'started', scoreGained: 0, mergeCount: 0, dropColumn: -1, dropRow: -1,
  removedTile: 0, introducedTile: 0, mergeSteps: [], tempoLevel: 1,
  dropDurationMilliseconds: 9000, dropTimerMilliseconds: 9000, message: 'Choose a column.',
}

const routes: readonly ArcadeRoute[] = [
  { name: 'Asteroids', path: '/games/asteroids', start: 'Casual run', board: '.ff-asteroids-replay-canvas', controls: '.ff-asteroids-replay-controls' },
  { name: 'Flappy', path: '/games/flappy', start: 'Start flight', board: '.ff-flappy-playfield', controls: '.ff-flappy-overlay button' },
  { name: 'Horse Flight', path: '/games/horse-flight', start: 'Start run', board: '.ff-horse-flight__field', controls: '.ff-horse-flight__touch-controls' },
  { name: 'Snake', path: '/games/snake', start: 'Play', board: '.ff-snake-board-wrap', controls: '.ff-snake-controls' },
  { name: '2048', path: '/games/2048', board: '.ff-2048-board-wrap', controls: '.ff-2048-controls' },
  { name: 'Drop Merge', path: '/games/drop-merge', board: '.ff-drop-merge-board-shell', controls: '.ff-drop-merge-column-board' },
]

for (const viewport of [
  { width: 1440, height: 900, desktop: true },
  { width: 1366, height: 768, desktop: true },
  { width: 390, height: 844, desktop: false },
  { width: 667, height: 375, desktop: false },
] as const) {
  test(`arcade play surfaces fit ${viewport.width}x${viewport.height}`, async ({ page }) => {
    test.setTimeout(45_000)
    await page.setViewportSize(viewport)
    await mockArcadeApi(page)

    for (const route of routes) {
      await test.step(route.name, async () => {
        await page.goto(route.path)
        const gameNavbar = page.locator('[data-game-navbar]')
        await expect(gameNavbar).toBeVisible()
        await expect(gameNavbar.getByRole('link', { name: 'Fortune Forge home' })).toHaveAttribute('href', '/home')
        await expect(gameNavbar.getByRole('link', { name: 'Other Games' })).toHaveAttribute('href', '/games')
        await expect(gameNavbar.locator('summary[aria-label="Account menu"]')).toBeVisible()
        if (route.name === 'Flappy') await expect(page.locator('.flappy-free-run-page__lobby-preview')).toBeVisible()
        if (route.start) await page.getByRole('button', { name: route.start, exact: true }).click()
        if (route.name === 'Drop Merge') {
          await expect(page.getByRole('button', { name: 'New run', exact: true })).toBeVisible()
          await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible()
          await expect(page.getByRole('button', { name: 'Undo', exact: true })).toHaveCount(0)
        }

        const board = page.locator(route.board)
        const controls = page.locator(route.controls)
        await expect(board).toBeVisible()
        await expect(controls).toBeVisible()
        if (route.name === 'Asteroids' && viewport.width === 1440) {
          await expect.poll(() => page.evaluate(() => {
            const resources = performance.getEntriesByType('resource').map(entry => entry.name)
            return ['ship-atlas', 'asteroid-atlas-v3', 'laser-impact-atlas', 'asteroid-explosion-atlas', 'powerup-atlas']
              .every(asset => resources.some(resource => resource.includes(asset)))
          })).toBe(true)
        }

        const geometry = await page.evaluate(({ boardSelector, controlsSelector }) => {
          const rect = (selector: string) => {
            const element = document.querySelector(selector)
            if (!(element instanceof HTMLElement)) return null
            const box = element.getBoundingClientRect()
            return { top: box.top, bottom: box.bottom, left: box.left, right: box.right, width: box.width, height: box.height }
          }
          return {
            htmlHeight: document.documentElement.scrollHeight,
            bodyHeight: document.body.scrollHeight,
            htmlWidth: document.documentElement.scrollWidth,
            board: rect(boardSelector),
            controls: rect(controlsSelector),
          }
        }, { boardSelector: route.board, controlsSelector: route.controls })

        expect(geometry.board, `${route.name} board`).not.toBeNull()
        expect(geometry.controls, `${route.name} controls`).not.toBeNull()
        expect(geometry.board!.width, `${route.name} board width`).toBeGreaterThan(40)
        expect(geometry.board!.height, `${route.name} board height`).toBeGreaterThan(40)
        expect(geometry.board!.top, `${route.name} board top`).toBeGreaterThanOrEqual(0)
        expect(geometry.board!.bottom, `${route.name} board bottom`).toBeLessThanOrEqual(viewport.height + 1)
        expect(geometry.controls!.top, `${route.name} controls top`).toBeGreaterThanOrEqual(0)
        expect(geometry.controls!.bottom, `${route.name} controls bottom`).toBeLessThanOrEqual(viewport.height + 1)

        if (viewport.desktop) {
          expect(geometry.htmlHeight, `${route.name} document height`).toBeLessThanOrEqual(viewport.height + 1)
          expect(geometry.bodyHeight, `${route.name} body height`).toBeLessThanOrEqual(viewport.height + 1)
          expect(geometry.htmlWidth, `${route.name} document width`).toBeLessThanOrEqual(viewport.width + 1)
          await page.evaluate(() => window.scrollTo(0, 9999))
          expect(await page.evaluate(() => window.scrollY), `${route.name} document scroll`).toBe(0)
        }
      })
    }
  })
}

async function mockArcadeApi(page: Page): Promise<void> {
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    let body: unknown

    if (path === '/api/accounts/me') body = account
    else if (path === '/api/games/2048/status') body = { available: true, size: 4, targetTile: 2048, mode: 'test' }
    else if (path.startsWith('/api/games/2048/games')) body = game2048
    else if (path === '/api/games/drop-merge/status') body = { available: true, columns: 7, rows: 7, firstBigTile: 1024, mode: 'test' }
    else if (path.startsWith('/api/games/drop-merge/games')) body = dropMerge
    else if (path === '/api/games/horse-flight/status') body = { available: true, tickMilliseconds: 20, balance: 1000, mode: 'test' }
    else if (path === '/api/games/horse-flight/runs' && method === 'POST') body = { runId: 'horse-test', seed: 123456, tickMilliseconds: 20 }
    else if (path === '/api/arcade-competitions/asteroids/free/runs' && method === 'POST') body = { runId: 'asteroids-test-01', seedHex: '0123456789abcdef', startedAtUtc: '2026-01-01T00:00:00Z', wasReplay: false }
    else if (path === '/api/arcade-competitions/flappy/free/runs' && method === 'POST') body = { runId: 'flappy-test-run-01', seed: 123456, startedAtUtc: '2026-01-01T00:00:00Z', wasReplay: false }
    else {
      await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'test-unavailable', message: 'Not needed for viewport testing.' }) })
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
}

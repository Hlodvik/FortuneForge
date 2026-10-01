import { expect, test, type Page } from '@playwright/test'
import type { CasinoWarRound, CasinoWarStatus } from '../../../game-packages/games/CasinoWar/client/FortuneForge.Games.CasinoWar.Client/src/contracts'
import type { AccountSummary } from '../../src/features/account/services/accountsApi'

const account: AccountSummary = {
  userId: 'war-ux-player', playerName: 'Alex', email: 'war-ux@example.test', createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 1_000, freeGames: 0 }, role: 'Player',
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}

const status: CasinoWarStatus = {
  available: true, minimumPrimaryStake: 1, maximumPrimaryStake: 100, stakeIncrement: 1,
  maximumTieStake: 25, balance: 1_000, mode: 'test',
}

const round: CasinoWarRound = {
  roundId: 'round-ux', balance: 1_008, primaryStake: 10, tieStake: 2, phase: 'completed',
  playerOpeningCard: { rank: 'ace', suit: 'clubs' }, dealerOpeningCard: { rank: 'king', suit: 'diamonds' },
  decision: null, playerWarCard: null, dealerWarCard: null,
  primarySettlement: { disposition: 'win', outcome: 'player-opening-win', totalWagered: 10, totalReturn: 20, profit: 10 },
  tieSettlement: { won: false, disposition: 'loss', outcome: 'player-win', stake: 2, totalReturn: 0, profit: -2 },
}

for (const viewport of [
  { name: 'phone', width: 390, height: 844 },
  { name: 'compact-phone', width: 320, height: 568 },
  { name: 'landscape', width: 667, height: 375 },
  { name: 'wide-landscape', width: 852, height: 393 },
] as const) {
  test(`Casino War main design fits ${viewport.name}`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await mockWar(page)
    await page.goto('/games/casino-war')

    const primary = page.getByRole('spinbutton', { name: 'Main bet', exact: true })
    const tie = page.getByRole('spinbutton', { name: 'Tie bet', exact: true })
    const deal = page.getByRole('button', { name: 'Deal', exact: true })
    await expect(primary).toBeEnabled()
    await primary.fill('')
    await expect(primary).toHaveValue('')
    await expect(deal).toBeDisabled()
    await primary.fill('10')
    await tie.fill('2')
    await expect(deal).toBeEnabled()
    await expect(page.getByText('R1,000.00', { exact: true })).toBeVisible()
    await fits(page, viewport)

    await deal.click()
    await expect(page.getByText('You win', { exact: true })).toBeVisible()
    await expect(page.getByText('R1,008.00', { exact: true })).toBeVisible()
    await fits(page, viewport)

    const help = page.getByRole('button', { name: 'How to play Casino War', exact: true })
    await help.click()
    await expect(page.getByRole('button', { name: 'Close rules', exact: true })).toBeFocused()
    await page.keyboard.press('Escape')
    await expect(help).toBeFocused()
  })
}

async function mockWar(page: Page) {
  await page.route('**/api/**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path === '/api/accounts/me') return route.fulfill({ json: account })
    if (path.endsWith('/status')) return route.fulfill({ json: status })
    if (path.endsWith('/rounds')) return route.fulfill({ json: round })
    return route.fulfill({ status: 404, json: { code: 'not-found', message: 'Not found.' } })
  })
}

async function fits(page: Page, viewport: { width: number; height: number }) {
  expect(await page.evaluate(() => Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))).toBeLessThanOrEqual(viewport.height + 1)
  const body = await page.locator('.in-game-shell__body').evaluate(element => ({ clientHeight: element.clientHeight, scrollHeight: element.scrollHeight }))
  expect(body.scrollHeight, JSON.stringify(body)).toBeLessThanOrEqual(body.clientHeight + 1)
  for (const surface of await page.locator('.ff-casino-war__table, .ff-casino-war__card, .ff-casino-war__controls, .ff-casino-war button, .ff-casino-war input').all()) {
    const bounds = await surface.boundingBox()
    expect(bounds).not.toBeNull()
    expect(bounds!.x).toBeGreaterThanOrEqual(-1)
    expect(bounds!.y).toBeGreaterThanOrEqual(-1)
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width + 1)
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height + 1)
    if (await surface.evaluate(element => element.tagName === 'BUTTON')) expect(bounds!.height).toBeGreaterThanOrEqual(44)
  }
}

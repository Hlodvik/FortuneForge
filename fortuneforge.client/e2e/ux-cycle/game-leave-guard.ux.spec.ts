import { expect, test, type Page } from '@playwright/test'
import { account, paths, houseRound, texasSession, heartsSession, slotCapture } from '../../../tools/navigation-review-fixtures.mjs'

type Game = 'house' | 'texas' | 'hearts'
type Mode = 'active' | 'idle' | 'result' | 'queue'
const viewports = [{ name: 'desktop', width: 1280, height: 720 }, { name: 'phone', width: 390, height: 844 }] as const

async function fixture(page: Page, game: Game, mode: Mode, phone = false) {
  const state = { mode, failReads: false, failMutation: false, balance: 1000,
    financialWrites: [] as string[], slotAccepted: false, slotCompleted: false, slotRequests: 0 }
  await page.addInitScript(() => {
    Object.defineProperty(window, 'AudioContext', { configurable: true, value: undefined })
    localStorage.setItem('fortuneforge:hearts:muted', 'true')
  })
  await page.route('**/api/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname
    const json = (value: unknown, status = 200) => route.fulfill({ status, json: value })
    if (path === '/api/accounts/me') return json({ ...account, balances: { ...account.balances, slotsCredits: state.balance } })
    if (path === '/api/games/experience') return json({ error: 'Experience metadata is intentionally unknown in the local navigation fixture' }, 503)
    if (path.includes('/onboarding')) return json({ shouldAskPlayedBefore: false, hasPlayedBefore: true })
    if (path === '/api/catalog/visibility') return json({ version: 1, games: Object.values(paths).concat('/slots/arcane-archives').map((route, index) => ({ id: `local-${index}`, route, visible: true })) })
    if (path === '/api/catalog/popularity') return json({ metric: 'recorded-plays', trackingStartedAtUtc: null, games: [] })
    if (path === '/api/catalog/recent-games') return json({ games: [] })
    if (path === '/api/payments/invoices') return json({ invoices: [] })
    if (path.includes('/history')) return json(path.includes('/accounts/') ? { spins: [] } : path.includes('/hearts/') ? { items: [] } : [])
    if (path === '/api/games/activity/presence') return route.fulfill({ status: 204 })
    if (path === '/api/slots/state') return json({ freeSpinsRemaining: 0, freeSpinWagerPoints: null, specialPointsBalance: 0, energyBalance: 0, sealCollections: [], freeSpinFeatureMode: null })
    if (path === '/api/slots/status' || path === '/api/slots/demo/status') return route.fulfill({ status: 204 })
    if (request.method() === 'POST') {
      state.financialWrites.push(path)
      if (path === '/api/slots/spins') {
        const body = request.postDataJSON()
        expect(body.wagerPoints).toBe(slotCapture.request.wagerPoints)
        state.slotRequests++; state.slotAccepted = true
        state.balance -= slotCapture.response.wagerPoints * slotCapture.response.pointValueInCents / 100
        await new Promise(resolve => setTimeout(resolve, 700))
        state.balance += slotCapture.response.payout.totalPoints * slotCapture.response.pointValueInCents / 100
        state.slotCompleted = true
        try { await json({ ...slotCapture.response, slotsCreditsBalance: state.balance }) } catch { /* A document navigation may close the response transport. */ }
        return
      }
      if (state.failMutation) { state.failReads = true; return route.abort('failed') }
      return json({ error: 'Unexpected financial action in navigation fixture' }, 409)
    }
    if (state.failReads && (path.includes('/house-holdem/') || path.includes('/texas-holdem/') || path.includes('/hearts/'))) return json({ error: 'Local transport interrupted' }, 503)
    if (path === '/api/games/house-holdem/status') return json({ available: true, balance: 1000, activeRoundId: state.mode === 'idle' ? null : 'local-open-hand', minimumAnte: 1, maximumAnte: 100, wagerIncrement: 1, maximumTrips: 500 })
    if (path.startsWith('/api/games/house-holdem/rounds/')) return json(houseRound(state.mode))
    if (path === '/api/cards/texas-holdem/credit/status') return json({ available: true, minimumStartPlayers: 3, maximumSeats: 5,
      minimumRealPlayers: 2, smallBlindCredits: .5, bigBlindCredits: 1, actionDeadlineSeconds: 30, matchDeadlineSeconds: 300 })
    if (path === '/api/cards/texas-holdem/credit/session') return json(texasSession(state.mode, phone ? 'all-in' : 'folded'))
    if (path === '/api/games/hearts/status') return json({ available: true, defaultTargetScore: 100, mode: 'free-play' })
    if (path === '/api/games/hearts/paid/session') return json(heartsSession(state.mode, phone))
    return json({ error: `No synthetic API fixture for ${path}` }, 503)
  })
  return state
}
const homeLink = (page: Page) => page.locator('[data-site-navbar] a[aria-label="Fortune Forge home"]')
async function protectedUnload(page: Page) {
  return page.evaluate(() => { const event = new Event('beforeunload', { cancelable: true }); dispatchEvent(event); return event.defaultPrevented })
}
async function ready(page: Page, game: Game, active: boolean) {
  await expect(homeLink(page)).toBeVisible()
  await expect.poll(() => protectedUnload(page)).toBe(active)
  if (!active) await expect(page.locator('[data-game-loading]')).toHaveCount(0)
}
async function programmatic(page: Page, destination?: string) {
  await page.evaluate(async href => {
    // Load the same live application module; no test-only production hook.
    const modulePath = '/src/features/navigation/gameLeaveNavigation.ts'
    const { requestGameNavigation } = await import(/* @vite-ignore */ modulePath)
    requestGameNavigation(() => { if (href) window.location.assign(href) })
  }, destination)
}

for (const viewport of viewports) {
  for (const game of ['house', 'texas', 'hearts'] as const) {
    test(`${game} ${viewport.name}: accepted waiting round confirms/cancels without corrupting Back history`, async ({ page }, info) => {
      await page.setViewportSize(viewport)
      const state = await fixture(page, game, 'active', viewport.name === 'phone')
      let nativeDialogs = 0
      page.on('dialog', dialog => { nativeDialogs++; void dialog.accept() })
      await page.goto('/home'); await page.goto(paths[game]); await ready(page, game, true)
      const navbarBefore = await page.locator('[data-site-navbar]').evaluate(node => node.outerHTML)
      await homeLink(page).click()
      const dialog = page.getByRole('dialog')
      await expect(dialog).toBeVisible(); await expect(dialog.getByRole('button', { name: 'Stay in game' })).toBeFocused()
      await page.keyboard.press('Shift+Tab'); await expect(dialog.getByRole('button', { name: 'Leave page' })).toBeFocused()
      await page.keyboard.press('Tab'); await expect(dialog.getByRole('button', { name: 'Stay in game' })).toBeFocused()
      const bounds = (await dialog.boundingBox())!
      expect(bounds.x).toBeGreaterThanOrEqual(0); expect(bounds.y).toBeGreaterThanOrEqual(0)
      expect(bounds.x + bounds.width).toBeLessThanOrEqual(viewport.width); expect(bounds.y + bounds.height).toBeLessThanOrEqual(viewport.height)
      await dialog.screenshot({ path: info.outputPath(`${game}-${viewport.name}-leave-dialog.png`) })
      await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0); await expect(homeLink(page)).toBeFocused()
      await homeLink(page).click(); await dialog.getByRole('button', { name: 'Stay in game' }).click()
      await expect(dialog).toHaveCount(0)
      expect(await page.locator('[data-site-navbar]').evaluate(node => node.outerHTML)).toBe(navbarBefore)
      for (let visit = 0; visit < 2; visit++) {
        await homeLink(page).click(); await dialog.getByRole('button', { name: 'Leave page' }).click()
        await page.waitForURL(/\/home$/)
        await page.goBack(); await expect(page).toHaveURL(new RegExp(paths[game] + '$')); await ready(page, game, true)
      }
      expect(nativeDialogs).toBe(0)
      state.mode = 'result'
      await page.reload(); await ready(page, game, false)
      await homeLink(page).click(); await page.waitForURL(/\/home$/)
      expect(state.financialWrites).toEqual([])
    })
    test(`${game} ${viewport.name}: idle participation leaves immediately`, async ({ page }) => {
      await page.setViewportSize(viewport); const state = await fixture(page, game, 'idle')
      await page.goto(paths[game]); await ready(page, game, false)
      await homeLink(page).click(); await page.waitForURL(/\/home$/)
      await expect(page.getByRole('dialog')).toHaveCount(0); expect(state.financialWrites).toEqual([])
    })
    test(`${game} ${viewport.name}: uncertain accepted request survives backgrounding and navigation choices`, async ({ page }) => {
      await page.setViewportSize(viewport); const state = await fixture(page, game, 'idle'); state.failMutation = true
      await page.goto(paths[game]); await ready(page, game, false)
      if (game === 'house') { await page.getByRole('button', { name: 'Place chip on Ante', exact: true }).click(); await page.getByRole('button', { name: 'Deal', exact: true }).click() }
      else await page.getByRole('button', { name: game === 'texas' ? /^Join / : /^Play for R/ }).first().click()
      await expect.poll(() => state.financialWrites.length).toBe(1); await expect.poll(() => protectedUnload(page)).toBe(true)
      await page.evaluate(() => { document.dispatchEvent(new Event('visibilitychange')); dispatchEvent(new Event('pagehide')) })
      await programmatic(page)
      await expect(page.getByRole('dialog')).toBeVisible()
      await page.getByRole('button', { name: 'Stay in game' }).click()
      expect(state.financialWrites).toHaveLength(1)
      await expect.poll(() => protectedUnload(page)).toBe(true)
    })
  }
  for (const game of ['texas', 'hearts'] as const) test(`${game} ${viewport.name}: reserved queue remains protected`, async ({ page }) => {
    await page.setViewportSize(viewport); const state = await fixture(page, game, 'queue')
    await page.goto(paths[game]); await ready(page, game, true); await homeLink(page).click()
    await expect(page.getByRole('dialog')).toBeVisible(); await page.getByRole('button', { name: 'Stay in game' }).click()
    expect(state.financialWrites).toEqual([])
  })
  test(`House ${viewport.name}: programmatic no-op leaves unload protection armed`, async ({ page }) => {
    await page.setViewportSize(viewport); await fixture(page, 'house', 'active')
    await page.goto(paths.house); await ready(page, 'house', true); await programmatic(page)
    await page.getByRole('button', { name: 'Leave page' }).click()
    await expect.poll(() => protectedUnload(page)).toBe(true)
    await programmatic(page, '/home'); await page.getByRole('button', { name: 'Leave page' }).click(); await page.waitForURL(/\/home$/)
    expect(await protectedUnload(page)).toBe(false)
  })
  test(`Slot ${viewport.name}: accepted transport settles once after immediate departure and balance reload`, async ({ page }) => {
    await page.setViewportSize(viewport); const state = await fixture(page, 'house', 'idle')
    await page.goto('/slots/arcane-archives'); const spin = page.getByRole('button', { name: 'Spin the reels', exact: true })
    await expect(spin).toBeEnabled()
    const decrease = page.getByRole('button', { name: 'Decrease wager', exact: true })
    for (let count = 0; count < 20 && await decrease.isEnabled(); count++) await decrease.click()
    await spin.click(); await expect.poll(() => state.slotAccepted).toBe(true)
    expect(await protectedUnload(page)).toBe(false)
    await programmatic(page, '/home'); await page.waitForURL(/\/home$/)
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect.poll(() => state.slotCompleted).toBe(true); expect(state.slotRequests).toBe(1)
    expect(state.balance).toBe(1000.5)
    await page.goto('/slots/arcane-archives'); await expect(spin).toBeEnabled()
    await expect(page.locator('[data-site-navbar]')).toContainText('1,000.50')
    expect(state.slotRequests).toBe(1)
  })
}

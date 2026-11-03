import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { expect, test, type Page } from '@playwright/test'

type Inventory = { nonSlotSmokeInventory: { smokePath: string; catalogPath?: string }[]; slotSmokeInventory: [string, string][] }
const inventory = await import(pathToFileURL(resolve(process.cwd(), '../scripts/game-smoke-inventory.mjs')).href) as Inventory
const productionRoutes = [...inventory.nonSlotSmokeInventory.map(item => item.catalogPath ?? item.smokePath), ...inventory.slotSmokeInventory.map(([id]) => `/slots/${id}`)]
const demoRoutes = [...inventory.slotSmokeInventory.map(([id]) => `/slots/${id}/demo`), '/demo/cards/blackjack', '/demo/cards/texas-holdem']
const directory = resolve(process.cwd(), '../output/maintenance-2026-11-01/navbar-consistency/browser')

type RecentPlay = { gameId: string; playedAtUtc: string }

async function fixture(page: Page, options: { recentGames?: RecentPlay[]; hiddenRoutes?: string[] } = {}) {
  const state = { balance: 1234.56, recent: options.recentGames ?? [], writes: [] as { path: string; body: unknown }[], pageErrors: [] as string[] }
  page.on('pageerror', error => state.pageErrors.push(error.message))
  await page.route('**/api/**', route => {
    const request = route.request(), path = new URL(request.url()).pathname
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (request.method() !== 'GET') state.writes.push({ path, body: request.postData() })
    if (path === '/api/accounts/me') return json({ userId: 'navbar-test', playerName: 'Shared Player', email: 'navbar@example.test', role: 'player', createdAtUtc: '2026-01-01T00:00:00Z',
      balances: { slotsCredits: state.balance, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } })
    if (path === '/api/catalog/visibility') return json({ version: 1, games: productionRoutes.map((route, index) => ({ id: `game-${index}`, route, visible: !options.hiddenRoutes?.includes(route) })) })
    if (path === '/api/catalog/popularity') return json({ metric: 'recorded-plays', trackingStartedAtUtc: null, games: [] })
    if (path === '/api/catalog/recent-games') return json({ games: state.recent })
    // Home's hosting-only mode reads genuine completed-result API contracts.
    // Seed those contracts too, rather than bypassing the recent activity hook.
    if (options.recentGames && path === '/api/cards/history') {
      const supported = new Set(['blackjack', 'texas-holdem', 'three-card-poker', 'house-holdem', 'solitaire', 'hi-lo', 'andar-bahar', 'mines'])
      return json(state.recent.filter(play => supported.has(play.gameId)).map((play, index) => ({
        resultId: `home-fixture-${index}`, game: play.gameId, mode: 'fixture', matchId: `completed-${index}`,
        startedAtUtc: play.playedAtUtc, completedAtUtc: play.playedAtUtc, unseen: false, requiresClaim: false,
        winningsCredits: 0, wagerCredits: 0, netCredits: 0, score: null, moves: null, schemaVersion: 1,
      })))
    }
    if (options.recentGames && path === '/api/accounts/me/history') return json({ spins: [] })
    if (path === '/api/game-reports' && request.method() === 'POST') return json({ id: 'a'.repeat(64), status: 'open', createdAtUtc: '2026-11-01T12:00:00Z' })
    // Every other API request stays here. Gameplay is deliberately unavailable;
    // route initialization, including its error states and CSS imports, is real.
    return json({ code: 'navbar-fixture-unavailable', message: 'Gameplay is disabled in the navbar fixture.' }, 503)
  })
  return state
}

async function observe(page: Page, account: boolean) {
  await expect(page.locator('.in-game-shell__surface:not(.route-state-shell)')).toBeVisible()
  const navbar = page.locator('[data-game-navbar]')
  await expect(navbar).toHaveCount(1)
  if (account) await expect(navbar.getByLabel('Account menu', { exact: true }).first()).toBeVisible()
  else await expect(navbar.getByLabel('Demo player', { exact: true })).toBeVisible()
  await page.evaluate(() => document.fonts.ready)
  await page.mouse.move(0, 200)
  const snapshot = () => navbar.evaluate(header => {
    const style = getComputedStyle(header), rect = header.getBoundingClientRect()
    const props = ['display', 'backgroundColor', 'color', 'fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'lineHeight', 'letterSpacing', 'textTransform', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'borderBottomColor', 'borderBottomWidth', 'borderRadius', 'boxSizing'] as const
    const parts = ['.site-navbar__brand', '.site-navbar__spade', '.site-navbar__brand strong', '.player-shell-header__games', '.in-game-navbar__report', '.site-header-balance', '.site-header-balance__full', '.site-header-balance__whole', '.site-navbar__avatar-letter', 'summary[aria-label="Account menu"]']
    return { connected: header.isConnected, classes: header.className, themedAncestor: !!header.closest('[data-game-surface]'), parent: header.parentElement?.className,
      styles: Object.fromEntries(props.map(prop => [prop, style[prop]])), x: rect.x, y: rect.y, width: rect.width, height: rect.height,
      partStyles: Object.fromEntries(parts.flatMap(selector => { const part = header.querySelector(selector); if (!part) return []; const css = getComputedStyle(part); return [[selector, Object.fromEntries(props.map(prop => [prop, css[prop]]))]] })),
      overflow: document.documentElement.scrollWidth > innerWidth,
      controls: [...header.querySelectorAll('a,button,summary')].filter(element => {
        const box = element.getBoundingClientRect()
        return box.width > 0 && box.height > 0 && !element.closest('details:not([open]) nav') && getComputedStyle(element).visibility !== 'hidden'
      }).map(element => {
        const box = element.getBoundingClientRect(); return { label: element.getAttribute('aria-label') ?? element.textContent,
          x: box.x, right: box.right, y: box.y, bottom: box.bottom,
          hit: element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)) }
      }) }
  })
  // Lazy-route/account initialization can replace the header between locator
  // resolution and evaluation. Acquire a live snapshot before comparing CSS.
  let result!: Awaited<ReturnType<typeof snapshot>>
  await expect(async () => {
    result = await snapshot()
    expect(result.connected).toBe(true)
    expect(result.width).toBe(page.viewportSize()!.width)
  }).toPass({ timeout: 15_000 })
  expect(result.classes).toBe('site-navbar site-navbar--game player-shell-header in-game-navbar')
  expect(result.themedAncestor).toBe(false)
  expect(result.parent).toBe('in-game-shell')
  expect(result.x).toBe(0); expect(result.y).toBe(0)
  expect(result.width).toBe(page.viewportSize()!.width)
  for (const control of result.controls) { expect(control.x, control.label ?? '').toBeGreaterThanOrEqual(0); expect(control.right).toBeLessThanOrEqual(page.viewportSize()!.width); expect(control.hit, control.label ?? '').toBe(true) }
  await expect(navbar.locator('.game-local-controls,.site-navbar__actions')).toHaveCount(0)
  await expect(navbar.getByLabel('Fortune Forge home', { exact: true })).toHaveAttribute('href', account ? '/home' : '/')
  await expect(navbar.locator('.player-shell-header__games')).toHaveAttribute('href', account ? '/home' : '/demo')
  await expect(navbar.getByLabel('Report this game', { exact: true })).toHaveCount(account ? 1 : 0)
  const balance = navbar.locator('.site-header-balance')
  if (account) { await expect(balance).toHaveAttribute('href', '/balance'); await expect(balance).toHaveAttribute('aria-label', 'Balance R1,234.56. Add funds') }
  else if (await balance.count()) expect(await balance.evaluate(node => node.tagName)).toBe('SPAN')
  return result
}

for (const viewport of [{ name: 'desktop', width: 1280, height: 720 }, { name: 'phone', width: 390, height: 844 }]) {
  test(`all production and demo game routes keep canonical ${viewport.name} chrome`, async ({ page }) => {
    test.setTimeout(240_000)
    await page.setViewportSize(viewport)
    const state = await fixture(page), records: unknown[] = []
    let sharedStyles: unknown
    const sharedParts = new Map<string, unknown>()
    await mkdir(directory, { recursive: true })
    try {
      for (const route of productionRoutes) {
        await page.goto(route)
        const record = await observe(page, true)
        sharedStyles ??= record.styles
        expect(record.styles, route).toEqual(sharedStyles)
        for (const [part, styles] of Object.entries(record.partStyles)) { if (!sharedParts.has(part)) sharedParts.set(part, styles); expect(styles, route + ' ' + part).toEqual(sharedParts.get(part)) }
        records.push({ route, ...record })
      }
      for (const route of demoRoutes) {
        await page.goto(route)
        const record = await observe(page, false)
        expect(record.styles, route).toEqual(sharedStyles)
        for (const [part, styles] of Object.entries(record.partStyles)) { if (!sharedParts.has(part)) sharedParts.set(part, styles); expect(styles, route + ' ' + part).toEqual(sharedParts.get(part)) }
        records.push({ route, ...record })
      }
      await page.screenshot({ path: resolve(directory, `${viewport.name}-demo.png`) })
    } finally {
      await writeFile(resolve(directory, `${viewport.name}-routes.json`), JSON.stringify({ records, ...state }, null, 2))
    }
    expect(records).toHaveLength(67)
  })
}

test('shared account menu, balances and reporting remain usable on desktop and mobile', async ({ page }) => {
  const state = await fixture(page)
  await mkdir(directory, { recursive: true })
  for (const viewport of [{ width: 1280, height: 720 }, { width: 320, height: 568 }, { width: 844, height: 390 }]) {
    await page.setViewportSize(viewport)
    await page.goto('/games/mines')
    await observe(page, true)
    const navbar = page.locator('[data-game-navbar]')
    await navbar.getByLabel('Account menu', { exact: true }).first().click()
    await expect(navbar.locator('details[open]')).toHaveCount(1)
    const menu = navbar.getByRole('navigation', { name: 'Account menu', exact: true })
    const menuBox = await menu.boundingBox()
    expect(menuBox).not.toBeNull()
    expect(menuBox!.x).toBeGreaterThanOrEqual(0)
    expect(menuBox!.x + menuBox!.width).toBeLessThanOrEqual(viewport.width)
    expect(menuBox!.y + menuBox!.height).toBeLessThanOrEqual(viewport.height)
    await expect(navbar.getByRole('link', { name: /Account settings/ })).toHaveAttribute('href', '/home/settings')
    await expect(navbar.getByRole('link', { name: /Request withdrawal/ })).toHaveAttribute('href', '/balance#withdrawal-request')
    await expect(navbar.getByRole('button', { name: /Log out/ })).toBeVisible()
    await navbar.getByRole('button', { name: /Log out/ }).scrollIntoViewIfNeeded()
    await page.screenshot({ path: resolve(directory, `shared-menu-${viewport.width}x${viewport.height}.png`) })
    await navbar.getByLabel('Account menu', { exact: true }).first().click()
    await navbar.getByLabel('Report this game', { exact: true }).click()
    await expect(page.getByRole('dialog')).toContainText('Report Mines')
    await page.getByRole('textbox', { name: 'Your report', exact: true }).fill('Local navbar regression fixture only.')
    await page.getByRole('button', { name: 'Send report', exact: true }).click()
    await expect(page.getByRole('dialog')).toContainText('Your report has been saved')
    await page.getByRole('button', { name: 'Done', exact: true }).click()
    await expect(navbar.getByLabel('Report this game', { exact: true })).toBeFocused()
    await page.screenshot({ path: resolve(directory, `shared-controls-${viewport.width}x${viewport.height}.png`) })
  }
  expect(state.writes.filter(write => write.path === '/api/game-reports')).toHaveLength(3)
  state.balance = 9876.54
  await page.reload()
  await expect(page.locator('[data-game-navbar] .site-header-balance')).toHaveAttribute('aria-label', 'Balance R9,876.54. Add funds')
})

test('Home sidebar orders Account, Add Funds and History while preserving navigation and game access', async ({ page }) => {
  const state = await fixture(page)
  await mkdir(directory, { recursive: true })
  for (const viewport of [{ width: 1280, height: 720 }, { width: 390, height: 844 }, { width: 320, height: 568 }]) {
    await page.setViewportSize(viewport)
    await page.goto('/home')
    const sidebar = page.locator('.home-lobby__sidebar')
    await expect(sidebar).toBeVisible()
    await expect(sidebar.getByRole('navigation', { name: 'Player navigation' }).getByRole('link')).toHaveCount(3)
    await expect(sidebar.getByRole('link', { name: 'Games', exact: true })).toHaveCount(0)
    const navigation = sidebar.getByRole('navigation', { name: 'Player navigation' })
    await expect(navigation.getByRole('link')).toHaveText(['Account', 'Add Funds', 'History'])
    for (const [label, href] of [['Account', '/home/settings'], ['Add Funds', '/balance'], ['History', '/home/history']]) {
      await expect(navigation.getByRole('link', { name: label, exact: true })).toBeVisible()
      await expect(navigation.getByRole('link', { name: label, exact: true })).toHaveAttribute('href', href)
    }
    await expect(sidebar.getByRole('button', { name: 'Help', exact: true })).toBeVisible()
    await expect(page.locator('.home-lobby__game-card').first()).toBeVisible()
    await sidebar.getByRole('button', { name: 'Help', exact: true }).click()
    await expect(page.getByRole('dialog', { name: 'What can we help with?' })).toBeVisible()
    await page.getByRole('button', { name: 'Close help', exact: true }).click()
    await page.screenshot({ path: resolve(directory, `sidebar-${viewport.width}x${viewport.height}.png`) })
    const addFunds = sidebar.getByRole('link', { name: 'Add Funds', exact: true })
    if (viewport.width === 320) await addFunds.press('Enter')
    else await addFunds.click()
    await expect(page).toHaveURL(/\/balance$/)
    await expect(page.getByRole('heading', { name: 'Balance', exact: true })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Balance actions' }).getByRole('button', { name: /^Deposit\b/ })).toBeVisible()
    expect(state.writes).toHaveLength(0)
  }
})

test('Home recent activity keeps the featured layout, responsive browsing and empty states', async ({ page }) => {
  const games = ['house-holdem', 'andar-bahar', 'blackjack', 'mines', 'texas-holdem', 'three-card-poker', 'hi-lo', 'solitaire']
  const state = await fixture(page, { hiddenRoutes: ['/cards/texas-holdem'], recentGames: [
    ...games.map((gameId, index) => ({ gameId, playedAtUtc: new Date(Date.UTC(2026, 10, 1, 12, 0) - index * 60_000).toISOString() })),
    { gameId: 'house-holdem', playedAtUtc: '2026-10-30T12:00:00Z' },
  ] })
  await mkdir(directory, { recursive: true })
  for (const viewport of [{ width: 1280, height: 900 }, { width: 390, height: 844 }, { width: 320, height: 568 }, { width: 844, height: 390 }]) {
    await page.setViewportSize(viewport)
    await page.goto('/home')
    const feature = page.getByRole('region', { name: 'Last played game', exact: true })
    await expect(feature.getByRole('heading', { name: 'House Hold’em', exact: true })).toBeVisible()
    await expect(feature.getByRole('link', { name: 'Play House Hold’em again', exact: true })).toHaveAttribute('href', '/cards/house-holdem')
    await expect(feature.locator('a a')).toHaveCount(0)
    const recent = feature.locator('.game-catalogue-rail__viewport')
    await expect(recent).toHaveAttribute('aria-label', 'Recently played')
    await expect(recent.getByRole('link')).toHaveCount(6)
    expect(await recent.getByRole('link').evaluateAll(links => links.map(link => link.getAttribute('href')))).toEqual([
      '/games/andar-bahar', '/cards/blackjack', '/games/mines', '/cards/three-card-poker', '/games/hi-lo', '/cards/solitaire',
    ])
    await expect(page.locator('.home-lobby__game-card')).toHaveCount(39)
    await expect(page.locator('.home-lobby__sidebar').getByRole('link', { name: 'Add Funds', exact: true })).toHaveAttribute('href', '/balance')
    await page.evaluate(() => document.fonts.ready)
    const art = await feature.locator('.home-lobby__featured-art').boundingBox()
    const copy = await feature.locator('.home-lobby__spotlight-copy').boundingBox()
    const row = await recent.boundingBox()
    expect(row!.y).toBeGreaterThanOrEqual(copy!.y + copy!.height)
    if (viewport.width > 680) expect(copy!.x).toBeGreaterThanOrEqual(art!.x + art!.width - 1)
    else expect(copy!.y).toBeGreaterThanOrEqual(art!.y + art!.height - 1)
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(viewport.width)
    const next = feature.getByRole('button', { name: 'Next games in Recently played', exact: true })
    await expect(next).toBeEnabled()
    const arrow = await next.boundingBox()
    expect(arrow!.width).toBeGreaterThanOrEqual(44)
    expect(arrow!.height).toBeGreaterThanOrEqual(44)
    await next.click()
    await expect.poll(() => recent.evaluate(element => element.scrollLeft)).toBeGreaterThan(0)
    await recent.press('Home')
    await expect.poll(() => recent.evaluate(element => element.scrollLeft)).toBe(0)
    await recent.press('End')
    await expect.poll(() => recent.evaluate(element => element.scrollWidth - element.clientWidth - element.scrollLeft)).toBeLessThanOrEqual(2)
    await expect(feature.getByRole('button', { name: 'Next games in Recently played', exact: true, includeHidden: true })).toBeDisabled()
    await recent.press('Home')
    await expect.poll(() => recent.evaluate(element => element.scrollLeft)).toBe(0)
    await page.screenshot({ path: resolve(directory, `home-recent-${viewport.width}x${viewport.height}.png`), fullPage: true })
  }
  state.recent = [state.recent[0]]
  await page.goto('/home')
  await expect(page.locator('.home-lobby__continue')).toBeVisible()
  await expect(page.locator('.home-lobby__recent')).toHaveCount(0)
  state.recent = []
  await page.reload()
  await expect(page.locator('.home-lobby__game-card')).toHaveCount(39)
  await expect(page.locator('.home-lobby__spotlights,.home-lobby__recent')).toHaveCount(0)
  expect(state.writes).toHaveLength(0)
  expect(state.pageErrors).toHaveLength(0)
})

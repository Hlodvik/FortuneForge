import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { account } from '../../tools/navigation-review-fixtures.mjs'
import type { SpinResult } from '../src/features/slots/types/slots'

const artifactDirectory = resolve(import.meta.dirname, '../../artifacts/fifth-sun-redesign/browser')
const publicRoute = '/slots/sunstone-eclipse'
const introductionKey = 'fortuneforge:fifth-sun:introduction:v2'
const money = (points: number, pointValue = 25) => 'R' + new Intl.NumberFormat('en-US').format(points * pointValue / 100)
type SpinRequest = { gameId: string; wagerPoints: number; useFreeSpin: boolean }
type Feature = { rows: number; isEclipseSpin: boolean; totalAwardPoints: number; contractVersion: number }
type NativeResponse = SpinResult & { spinFeature: Feature }
type Capture = { source: unknown; sourceFiles: string[]; initialState: Record<string, unknown>; response: NativeResponse }
type Review = { requests: SpinRequest[]; mismatches: string[]; unexpectedApis: string[]; pageErrors: string[]; imageFailures: string[] }

function readJson<T>(path: string): T {
  return JSON.parse(readFileSync(resolve(import.meta.dirname, '../..', path), 'utf8')) as T
}

function nativeCapture(mode: 'paid' | 'free'): Capture {
  const prefix = 'tools/fixtures/slot-contracts/fifth-sun/native-' + mode
  const origin = readJson<{ startingState: Record<string, unknown> }>(prefix + '-origin.json')
  return { source: origin, sourceFiles: [prefix + '-origin.json', prefix + '-spin.json'], initialState: origin.startingState,
    response: readJson<NativeResponse>(prefix + '-spin.json') }
}

function historicalCapture(): Capture {
  const capture = readJson<{ source: unknown; initialState: Record<string, unknown>; steps: { response: NativeResponse }[] }>(
    'tools/fixtures/slot-contracts/sunstone-eclipse/free-eclipse.json')
  return { source: capture.source, sourceFiles: ['tools/fixtures/slot-contracts/sunstone-eclipse/free-eclipse.json'],
    initialState: capture.initialState, response: capture.steps[0].response }
}

/** All API traffic terminates in this browser's routes; native receipts are replayed without changing board, amounts or identities. */
async function mockGame(page: Page, capture: Capture): Promise<Review> {
  const review: Review = { requests: [], mismatches: [], unexpectedApis: [], pageErrors: [], imageFailures: [] }
  page.on('pageerror', error => review.pageErrors.push(error.message))
  page.on('response', response => {
    if (response.request().resourceType() === 'image' && response.status() >= 400) review.imageFailures.push(response.url())
  })
  await page.route('**/api/**', route => {
    review.unexpectedApis.push(route.request().method() + ' ' + new URL(route.request().url()).pathname)
    return route.fulfill({ status: 404, json: { error: 'This review has no fixture for that API.' } })
  })
  await page.route('**/api/accounts/me', route => route.fulfill({ json: {
    ...account, balances: { ...account.balances, slotsCredits: capture.initialState.slotsCreditsBalance },
  } }))
  await page.route('**/api/games/experience', route => route.fulfill({ status: 401 }))
  await page.route('**/api/catalog/visibility', route => route.fulfill({ json: {
    version: 1, games: [{ id: 'robot-revolution', route: publicRoute, visible: true }],
  } }))
  await page.route('**/api/games/activity/presence', route => route.fulfill({ status: 204 }))
  await page.route('**/api/slots/status**', route => route.fulfill({ status: 204 }))
  await page.route('**/api/payments/invoices?**', route => route.fulfill({ json: { invoices: [] } }))
  await page.route('**/api/slots/state?**', route => route.fulfill({ json: {
    specialPointsBalance: 0, energyBalance: 0, sealCollections: [], ...capture.initialState,
  } }))
  await page.route('**/api/slots/spins', route => {
    const request = route.request().postDataJSON() as SpinRequest
    review.requests.push(request)
    const response = capture.response
    if (request.gameId !== response.gameId || request.wagerPoints !== response.wagerPoints ||
      Boolean(request.useFreeSpin) !== response.isFreeSpin) {
      review.mismatches.push(JSON.stringify({ request, captured: {
        gameId: response.gameId, wagerPoints: response.wagerPoints, useFreeSpin: response.isFreeSpin,
      } }))
      return route.fulfill({ status: 422, json: { error: 'Select the exact native capture terms; fixture amounts cannot be rescaled.' } })
    }
    return route.fulfill({ json: structuredClone(response) })
  })
  return review
}

async function openWithoutIntroduction(page: Page) {
  await page.addInitScript(key => localStorage.setItem(key, '1'), introductionKey)
  await page.goto(publicRoute)
  await ready(page)
}

async function ready(page: Page) {
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
  await page.waitForFunction(() => [...document.querySelectorAll<HTMLImageElement>(
    '.slots-page img, .win-help img, .symbol-value-guide img',
  )].every(image => image.complete && image.naturalWidth > 0))
  await page.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>(done => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
}

async function screenshot(page: Page, name: string) {
  await mkdir(artifactDirectory, { recursive: true })
  await page.screenshot({ path: resolve(artifactDirectory, name + '.png'), fullPage: true })
}

async function inspectLayout(page: Page, name: string, stacked: boolean) {
  const metrics = await page.evaluate(() => {
    const box = (element: Element) => {
      const bounds = element.getBoundingClientRect()
      return { left: bounds.left, right: bounds.right, top: bounds.top, bottom: bounds.bottom, width: bounds.width, height: bounds.height }
    }
    const get = (selector: string) => box(document.querySelector(selector)!)
    const wheel = document.querySelector('.sunwheel')!
    const targets = [...wheel.querySelectorAll('.sunwheel-guardian, .sunwheel__result, .sunwheel__hint')]
    const clipping = targets.flatMap(target => {
      const targetBounds = target.getBoundingClientRect()
      let ancestor = target.parentElement
      const failures = []
      while (ancestor && ancestor !== document.querySelector('.slots-page')) {
        const style = getComputedStyle(ancestor)
        const bounds = ancestor.getBoundingClientRect()
        if (/hidden|clip/.test(style.overflowY) && (targetBounds.top < bounds.top - 2 || targetBounds.bottom > bounds.bottom + 2))
          failures.push({ target: target.className, ancestor: ancestor.className, targetBounds: box(target), bounds: box(ancestor) })
        ancestor = ancestor.parentElement
      }
      return failures
    })
    const controls = ['Symbol values', 'How to win', 'Open settings', 'Report this game'].flatMap(label => {
      const element = [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.getAttribute('aria-label') === label)
      if (!element) return []
      const bounds = element.getBoundingClientRect()
      const hit = document.elementFromPoint(bounds.left + bounds.width / 2, bounds.top + bounds.height / 2)
      return [{ label, ...box(element), hit: Boolean(hit && element.contains(hit)) }]
    })
    const page = document.querySelector<HTMLElement>('.slots-page')!
    const symbolOverflows = [...document.querySelectorAll('.slot-symbol[data-row-index]')].flatMap(cell => {
      const image = cell.querySelector('.slot-symbol__image')
      if (!image) return []
      const cellBounds = cell.getBoundingClientRect()
      const imageBounds = image.getBoundingClientRect()
      return imageBounds.top < cellBounds.top - 2 || imageBounds.bottom > cellBounds.bottom + 2 ||
        imageBounds.left < cellBounds.left - 2 || imageBounds.right > cellBounds.right + 2
        ? [{ reel: cell.getAttribute('data-reel-index'), row: cell.getAttribute('data-row-index'), cell: box(cell), image: box(image) }] : []
    })
    return { viewport: { width: innerWidth, height: innerHeight }, documentWidth: document.documentElement.scrollWidth,
      bodyWidth: document.body.scrollWidth, pageWidth: page.scrollWidth, pageHeight: page.scrollHeight, pageClientHeight: page.clientHeight,
      cabinet: get('.slots-page__cabinet-spotlight'), wheel: get('.sunwheel'), playbar: get('.slots-page__playbar'),
      guardians: [...wheel.querySelectorAll('.sunwheel-guardian')].map(box), calendar: get('.sunwheel-calendar'),
      result: get('.sunwheel__result'), clipping, symbolOverflows, controls }
  })
  await mkdir(artifactDirectory, { recursive: true })
  await writeFile(resolve(artifactDirectory, name + '-layout.json'), JSON.stringify(metrics, null, 2))
  expect(metrics.documentWidth).toBe(metrics.viewport.width)
  expect(metrics.bodyWidth).toBe(metrics.viewport.width)
  expect(metrics.pageWidth).toBeLessThanOrEqual(metrics.viewport.width)
  expect(metrics.clipping, 'Every guardian and award/hint strip must survive its clipping ancestors').toEqual([])
  expect(metrics.symbolOverflows.length, 'Every symbol image must fit its own row, including the bottom row: ' +
    JSON.stringify(metrics.symbolOverflows.slice(0, 2))).toBe(0)
  expect(metrics.guardians).toHaveLength(3)
  for (const guardian of metrics.guardians) {
    const overlap = Math.max(0, Math.min(metrics.calendar.right, guardian.right) - Math.max(metrics.calendar.left, guardian.left)) *
      Math.max(0, Math.min(metrics.calendar.bottom, guardian.bottom) - Math.max(metrics.calendar.top, guardian.top))
    expect(overlap, 'Calendar must not cover guardian buttons in either layout').toBe(0)
  }
  expect(metrics.guardians[2].bottom, 'Guardian rows must not cover the award strip').toBeLessThanOrEqual(metrics.result.top + 2)
  expect(metrics.playbar.top).toBeGreaterThanOrEqual(metrics.cabinet.bottom - 2)
  if (stacked) {
    expect(metrics.wheel.top).toBeGreaterThanOrEqual(metrics.playbar.bottom - 2)
    expect(metrics.pageHeight).toBeGreaterThan(metrics.pageClientHeight)
  } else {
    expect(metrics.cabinet.width).toBeGreaterThan(metrics.wheel.width * 1.4)
    expect(Math.abs(metrics.cabinet.top - metrics.wheel.top)).toBeLessThanOrEqual(2)
    expect(Math.abs(metrics.cabinet.bottom - metrics.wheel.bottom)).toBeLessThanOrEqual(2)
    expect(metrics.cabinet.right).toBeLessThan(metrics.wheel.left)
    expect(metrics.playbar.bottom).toBeLessThanOrEqual(metrics.viewport.height)
    expect(metrics.wheel.bottom).toBeLessThanOrEqual(metrics.viewport.height)
  }
  for (const [index, control] of metrics.controls.entries()) {
    expect(control.left).toBeGreaterThanOrEqual(0)
    expect(control.right).toBeLessThanOrEqual(metrics.viewport.width)
    expect(control.top).toBeGreaterThanOrEqual(0)
    expect(control.bottom).toBeLessThanOrEqual(metrics.viewport.height)
    expect(control.hit, control.label + ' must receive its own pointer hit').toBe(true)
    for (const other of metrics.controls.slice(index + 1)) {
      const overlap = Math.max(0, Math.min(control.right, other.right) - Math.max(control.left, other.left)) *
        Math.max(0, Math.min(control.bottom, other.bottom) - Math.max(control.top, other.top))
      expect(overlap, control.label + ' must remain clear of ' + other.label).toBe(0)
    }
  }
  expect(metrics.controls.map(control => control.label)).toEqual(['Symbol values', 'How to win', 'Open settings', 'Report this game'])
  return metrics
}

async function spinNative(page: Page, capture: Capture, review: Review, count: number, keyboard = false) {
  const spin = page.getByRole('button', { name: 'Spin the reels', exact: true })
  if (keyboard) await page.keyboard.press('Space')
  else await spin.click()
  await expect.poll(() => review.requests.length).toBe(count)
  await expect(spin).toBeEnabled()
  await expect(page.locator('.sunwheel')).not.toHaveAttribute('data-spinning', 'true')
  await expect(page.locator('.sunwheel__result')).not.toContainText('unavailable')
  const award = capture.response.spinFeature.totalAwardPoints
  await expect(page.getByLabel('Sunwheel award', { exact: true })).toHaveText(
    award > 0 ? '+' + money(award, capture.response.pointValueInCents) : 'SUNWHEEL')
  await expect(page.locator('.slot-symbol[data-reel-index][data-row-index]')).toHaveCount(capture.response.spinFeature.rows * 5)
  expect(review.mismatches).toEqual([])
}

async function finishReview(review: Review, capture: Capture, name: string) {
  await writeFile(resolve(artifactDirectory, name + '-receipt.json'), JSON.stringify({
    source: capture.source, sourceFiles: capture.sourceFiles, nativeSpinId: capture.response.spinId,
    replayNote: 'An exact native receipt may be replayed for repeated UI requests. No response field or payout is rescaled.',
    feature: capture.response.spinFeature, ...review,
  }, null, 2))
  expect(review.pageErrors).toEqual([])
  expect(review.imageFailures).toEqual([])
  expect(review.unexpectedApis).toEqual([])
  expect(review.mismatches).toEqual([])
}

test.afterEach(async ({ page }, info: TestInfo) => {
  await mkdir(artifactDirectory, { recursive: true })
  if (info.status !== 'passed') {
    await page.screenshot({ path: resolve(artifactDirectory, info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase() + '-failure.png'), fullPage: true })
    return
  }
  const path = resolve(artifactDirectory, 'unique-green-ledger.json')
  let entries: { title: string; checkedUtc: string }[] = []
  try { entries = JSON.parse(await readFile(path, 'utf8')) } catch { /* First new review run. */ }
  if (!entries.some(entry => entry.title === info.title)) entries.push({ title: info.title, checkedUtc: new Date().toISOString() })
  await writeFile(path, JSON.stringify(entries, null, 2))
})

test.describe('wide desktop', () => {
  test.use({ viewport: { width: 1920, height: 1024 }, reducedMotion: 'no-preference' })
  test('new Sunstone Eclipse introduction, controls, keyboard and ambient win', async ({ page }) => {
    const capture = nativeCapture('paid')
    expect(capture.response.wagerPoints).toBe(12)
    const review = await mockGame(page, capture)
    await page.goto(publicRoute)
    const intro = page.getByRole('dialog', { name: 'Welcome to Sunstone Eclipse', exact: true })
    await expect(intro).toBeVisible()
    await ready(page)
    await expect(intro.locator('.fifth-sun-intro__hero-copy h2')).toHaveText('Sunstone Eclipse')
    await expect(intro.locator('.fifth-sun-intro__hero-art img')).toHaveAttribute('src', /sunstone-eclipse-scatter-v1/)
    await expect(intro.locator('.fifth-sun-intro__choices img')).toHaveCount(3)
    for (const image of await intro.locator('.fifth-sun-intro__choices img').all()) await expect(image).toHaveAttribute('src', /-v3/)
    const tint = await page.locator('.win-help__backdrop').evaluate(element => getComputedStyle(element).backgroundImage)
    expect(tint).toContain('url(')
    expect(tint).toContain('0.92')
    expect(await intro.locator('h2').last().evaluate(element => getComputedStyle(element).fontFamily)).toContain('Sunstone Eclipse Display')
    await screenshot(page, 'desktop-introduction')
    const diagram = intro.locator('.fifth-sun-intro__path-board polyline')
    await intro.getByRole('button', { name: 'Eagle', exact: true }).click()
    const before = await diagram.getAttribute('points')
    await intro.getByRole('button', { name: 'Rotate example calendar', exact: true }).click()
    await expect(diagram).not.toHaveAttribute('points', before!)
    await expect(intro.getByRole('status')).toContainText('Eagle')
    await intro.getByRole('button', { name: 'Enter the Eclipse', exact: false }).click()
    await expect(intro).toHaveCount(0)
    await ready(page)
    await expect(page.locator('.symbol-value-guide--side')).toHaveCount(0)
    const values = page.getByRole('button', { name: 'Symbol values', exact: true })
    await expect(values).toHaveCount(1)
    await expect(values).toHaveAttribute('aria-expanded', 'false')
    await expect(page.getByRole('dialog', { name: 'Symbol values', exact: true })).toHaveCount(0)
    await expect(page.locator('.payment-alerts')).toHaveCount(0)
    await inspectLayout(page, 'desktop-idle', false)
    await screenshot(page, 'desktop-idle')
    await values.click()
    const guide = page.getByRole('dialog', { name: 'Symbol values', exact: true })
    await expect(guide).toBeVisible()
    expect(await guide.evaluate(element => getComputedStyle(element).backgroundColor)).toBe('rgb(23, 17, 13)')
    for (const glyph of ['flame', 'rain', 'wind', 'earth', 'reed'])
      await expect(guide.locator('.symbol-value-guide__item--glyph_' + glyph)).toHaveCount(1)
    const contract = readJson<{ games: { gameId: string; payoutMultiplier: number; paylinePayouts: Record<string, { three: number; five: number }> }[] }>(
      'packages/slot-game-runtime/src/contracts/engine-guide-contracts.json').games.find(game => game.gameId === capture.response.gameId)!
    expect(contract.payoutMultiplier).toBe(1.14)
    for (const id of ['GLYPH_FLAME', 'GLYPH_RAIN', 'GLYPH_WIND', 'GLYPH_EARTH', 'GLYPH_REED', 'JAGUAR', 'SUNSTONE', 'WILD']) {
      const row = guide.locator('.symbol-value-guide__item--' + id.toLowerCase())
      const payout = contract.paylinePayouts[id]
      await expect(row.locator('.symbol-value-guide__award--three')).toContainText(money(Math.round(12 * payout.three * contract.payoutMultiplier)))
      await expect(row.locator('.symbol-value-guide__award--five')).toContainText(money(Math.round(12 * payout.five * contract.payoutMultiplier)))
    }
    expect(contract.paylinePayouts.SUNSTONE.five).toBeGreaterThan(contract.paylinePayouts.JAGUAR.five)
    await screenshot(page, 'desktop-symbol-values')
    await page.keyboard.press('Escape')
    await expect(guide).toHaveCount(0)
    await expect(values).toBeFocused()
    const help = page.getByRole('button', { name: 'How to win', exact: true })
    await help.click()
    await expect(intro).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(help).toBeFocused()
    const settings = page.getByRole('button', { name: 'Open settings', exact: true })
    await settings.click()
    const settingsDialog = page.locator('.game-settings[role="dialog"]')
    await expect(settingsDialog).toBeVisible()
    expect(await settingsDialog.evaluate(element => getComputedStyle(element).backgroundColor)).toBe('rgb(23, 17, 13)')
    await page.keyboard.press('Escape')
    await expect(settings).toBeFocused()
    const bet = page.getByRole('button', { name: /^Change bet, currently / })
    await expect(bet).toHaveAttribute('aria-label', 'Change bet, currently R3')
    await bet.click()
    const chooser = page.getByRole('dialog', { name: 'Choose bet', exact: true })
    const slider = chooser.getByRole('slider', { name: 'Bet amount', exact: true })
    await slider.press('End')
    await expect(bet).toHaveAttribute('aria-label', 'Change bet, currently R75')
    await slider.press('Home')
    await expect(bet).toHaveAttribute('aria-label', 'Change bet, currently R3')
    await page.keyboard.press('Escape')
    await expect(bet).toBeFocused()
    await spinNative(page, capture, review, 1)
    const ambient = page.locator('.slots-page__ambient-win')
    await expect(ambient).toBeVisible()
    await expect(ambient).toContainText(/R/)
    await expect(page.getByText(/spin again/i)).toHaveCount(0)
    const amount = page.locator('.slot-winning-paylines__amount').first()
    await expect(amount).toBeVisible()
    await expect(amount).toHaveCSS('animation-name', 'fifth-sun-payline-breathe')
    await screenshot(page, 'desktop-settled')
    await inspectLayout(page, 'desktop-settled', false)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await expect(amount).toHaveCSS('animation-name', 'none')
    await expect(ambient).toHaveCSS('animation-name', 'none')
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    await page.keyboard.press('ArrowRight')
    await expect(page.getByRole('button', { name: 'Change bet, currently R3.5', exact: true })).toBeVisible()
    await page.keyboard.press('ArrowLeft')
    await expect(bet).toHaveAttribute('aria-label', 'Change bet, currently R3')
    await spinNative(page, capture, review, 2, true)
    expect(review.requests).toHaveLength(2)
    await finishReview(review, capture, 'desktop')
  })
})

for (const viewport of [
  { name: 'laptop', width: 1366, height: 768, stacked: false },
  { name: 'phone', width: 390, height: 844, stacked: true },
]) {
  test.describe(viewport.name, () => {
    test.use({ viewport: { width: viewport.width, height: viewport.height }, hasTouch: viewport.stacked, isMobile: viewport.stacked })
    test('new ' + viewport.name + ' cabinet, Sunwheel and utility layout', async ({ page }) => {
      const capture = nativeCapture('paid')
      const review = await mockGame(page, capture)
      await openWithoutIntroduction(page)
      await screenshot(page, viewport.name + '-top')
      await inspectLayout(page, viewport.name + '-idle', viewport.stacked)
      if (viewport.stacked) {
        await page.locator('.slots-page').evaluate(element => element.scrollTo({ top: element.scrollHeight }))
        await expect(page.locator('.sunwheel__result')).toBeInViewport()
        await expect(page.locator('.sunwheel-guardian').last()).toBeInViewport()
        await screenshot(page, 'phone-sunwheel')
      }
      expect(review.requests).toHaveLength(0)
      await finishReview(review, capture, viewport.name)
    })
  })
}

test.describe('new expanded phone bonus', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true })
  test('new six-row Sunstone Eclipse free spin keeps native wager and readable stacked panels', async ({ page }) => {
    const capture = nativeCapture('free')
    expect(capture.response.wagerPoints).toBe(12)
    expect(capture.response.spinFeature.contractVersion).toBe(2)
    expect(capture.response.spinFeature.rows).toBe(6)
    const review = await mockGame(page, capture)
    await openWithoutIntroduction(page)
    await expect(page.getByRole('button', { name: /^(?:Bet locked at |Change bet, currently )R3$/ })).toBeDisabled()
    await spinNative(page, capture, review, 1)
    await expect(page.locator('.sunwheel')).toHaveAttribute('data-active-round', 'true')
    await expect(page.locator('.sunwheel__mode')).toHaveText('Eclipse spins · double wheel awards')
    await screenshot(page, 'phone-expanded-reels')
    await inspectLayout(page, 'phone-expanded', true)
    await page.locator('.slots-page').evaluate(element => element.scrollTo({ top: element.scrollHeight }))
    await expect(page.locator('.sunwheel__result')).toBeInViewport()
    await screenshot(page, 'phone-expanded-sunwheel')
    await finishReview(review, capture, 'phone-expanded')
  })
})

test.describe('saved Sunstone compatibility', () => {
  test.use({ viewport: { width: 1366, height: 768 } })
  test('one owed legacy Sunstone free spin retains its captured symbols and locked wager', async ({ page }) => {
    const capture = historicalCapture()
    expect(capture.response.symbolSetId).toBe('classic-symbols-v3')
    expect(capture.response.wagerPoints).toBe(100)
    const review = await mockGame(page, capture)
    await openWithoutIntroduction(page)
    await expect(page.getByRole('button', { name: /^(?:Bet locked at |Change bet, currently )R25$/ })).toBeDisabled()
    await spinNative(page, capture, review, 1)
    await expect(page.locator('.sunwheel')).toHaveAttribute('data-active-round', 'true')
    await expect(page.locator('.sunwheel__result')).not.toContainText('unavailable')
    await inspectLayout(page, 'legacy-carry', false)
    await screenshot(page, 'legacy-carry')
    expect(review.requests[0]).toMatchObject({ gameId: 'robot-revolution-v1', wagerPoints: 100, useFreeSpin: true })
    await finishReview(review, capture, 'legacy-carry')
  })
})

test.describe('new narrow animation proof', () => {
  test.use({ viewport: { width: 1366, height: 768 }, reducedMotion: 'no-preference' })
  test('actual win keyframes change size and gold turquoise paint with a static reduced-motion state', async ({ page }) => {
    const capture = nativeCapture('paid')
    const review = await mockGame(page, capture)
    await openWithoutIntroduction(page)
    await spinNative(page, capture, review, 1)
    const amount = page.locator('.slot-winning-paylines__amount').first()
    const ambient = page.locator('.slots-page__ambient-win')
    await expect(amount).toBeVisible()
    await expect(ambient).toBeVisible()
    const keyframes = async (selector: string, name: string) => page.locator(selector).first().evaluate((element, animationName) =>
      element.getAnimations().filter(animation => (animation as CSSAnimation).animationName === animationName)
        .flatMap(animation => animation.effect?.getKeyframes() ?? [])
        .map(frame => ({ fill: String(frame.fill ?? ''), scale: String(frame.scale ?? ''), opacity: String(frame.opacity ?? '') })), name)
    const amountFrames = await keyframes('.slot-winning-paylines__amount', 'fifth-sun-payline-breathe')
    const ambientFrames = await keyframes('.slots-page__ambient-win', 'fifth-sun-gold-breathe')
    expect(new Set(amountFrames.map(frame => frame.scale).filter(Boolean)).size).toBeGreaterThan(1)
    expect(new Set(ambientFrames.map(frame => frame.scale).filter(Boolean)).size).toBeGreaterThan(1)
    const colors = [...new Set(amountFrames.map(frame => frame.fill).filter(Boolean))]
    expect(colors.length).toBeGreaterThan(1)
    const channels = colors.map(color => /^#[\da-f]{6}$/i.test(color)
      ? [1, 3, 5].map(offset => parseInt(color.slice(offset, offset + 2), 16))
      : color.match(/\d+(?:\.\d+)?/g)!.slice(0, 3).map(Number))
    expect(channels.some(([red, green, blue]) => red > green && green > blue), 'A gold phase must exist').toBe(true)
    expect(channels.some(([red, green, blue]) => green > red && blue > red), 'A turquoise phase must exist').toBe(true)
    await screenshot(page, 'animation-win')
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await expect(amount).toHaveCSS('animation-name', 'none')
    await expect(ambient).toHaveCSS('animation-name', 'none')
    await expect(amount).toHaveCSS('scale', '1')
    await expect(ambient).toHaveCSS('scale', '1')
    const reducedPaint = await amount.evaluate(element => {
      const style = getComputedStyle(element)
      const context = document.createElement('canvas').getContext('2d')!
      context.fillStyle = style.fill
      const actual = context.fillStyle
      context.fillStyle = style.getPropertyValue('--slot-payline-base')
      return { actual, cabinetGold: context.fillStyle }
    })
    expect(reducedPaint.actual).toBe(reducedPaint.cabinetGold)
    await writeFile(resolve(artifactDirectory, 'actual-animation-keyframes.json'), JSON.stringify({
      source: capture.sourceFiles, amountFrames, ambientFrames, reducedPaint,
      mechanism: 'CSSAnimation.effect.getKeyframes() from the real presented native result; no synthetic animation example.',
    }, null, 2))
    await finishReview(review, capture, 'animation')
  })
})

test.describe('new footer placement proof', () => {
  test.use({ viewport: { width: 1920, height: 1024 }, reducedMotion: 'no-preference' })
  test('desktop ambient win stays between spin controls and keyboard hints at peak size', async ({ page }) => {
    const capture = nativeCapture('paid')
    const review = await mockGame(page, capture)
    await openWithoutIntroduction(page)
    await spinNative(page, capture, review, 1)
    const ambient = page.locator('.slots-page__ambient-win')
    await expect(ambient).toBeVisible()
    const measurements = []
    for (const viewport of [{ width: 1920, height: 1024 }, { width: 1366, height: 768 }]) {
      await page.setViewportSize(viewport)
      const metrics = await ambient.evaluate(element => {
        for (const animation of element.getAnimations()) {
          if ((animation as CSSAnimation).animationName !== 'fifth-sun-gold-breathe') continue
          animation.pause()
          animation.currentTime = Number(animation.effect!.getTiming().duration) / 2
        }
        const box = (target: Element) => {
          const bounds = target.getBoundingClientRect()
          return { left: bounds.left, right: bounds.right, top: bounds.top, bottom: bounds.bottom, width: bounds.width, height: bounds.height }
        }
        return { viewport: { width: innerWidth, height: innerHeight }, ambient: box(element),
          scale: getComputedStyle(element).scale, playbar: box(document.querySelector('.slots-page__playbar')!),
          keyboard: box(document.querySelector('.slots-page__keyboard-help')!),
          keyboardItems: [...document.querySelectorAll('.slots-page__keyboard-help > *')].map(item => {
            const range = document.createRange()
            range.selectNodeContents(item)
            const content = range.getBoundingClientRect()
            return { ...box(item), content: { top: content.top, bottom: content.bottom, left: content.left, right: content.right } }
          })
            .filter(item => item.width > 0 && item.height > 0) }
      })
      measurements.push(metrics)
      await screenshot(page, 'footer-' + viewport.width + '-settled')
      await writeFile(resolve(artifactDirectory, 'footer-placement.json'), JSON.stringify({
        measurements, method: 'One exact mocked native paid result, same page resized without another spin; real CSS animation paused at its 50% size peak. DOMRange measures painted keyboard content; transparent parent and 44px Mute hit areas remain recorded separately. The win output has no pointer events.',
      }, null, 2))
      expect(metrics.scale).toBe('1.025')
      expect(metrics.ambient.top, 'Peak-size win text must sit below spin controls').toBeGreaterThan(metrics.playbar.bottom + 1)
      expect(metrics.keyboardItems.length).toBeGreaterThan(0)
      expect(metrics.ambient.bottom, 'Peak-size win text must sit above visible keyboard hints')
        .toBeLessThan(Math.min(...metrics.keyboardItems.map(item => item.content.top)) - 1)
      await expect(ambient).toHaveCSS('pointer-events', 'none')
    }
    expect(review.requests).toHaveLength(1)
    await finishReview(review, capture, 'footer-placement')
  })
})

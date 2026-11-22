import { expect, test, type Page } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { account } from '../../tools/navigation-review-fixtures.mjs'
import type { SpinResult } from '../src/features/slots/types/slots'
import type { SunwheelFeature } from '../../games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/sunwheel'

const screenshotDirectory = resolve(import.meta.dirname, '../../artifacts/sunstone-eclipse')
const introductionKey = 'fortuneforge:sunstone-eclipse:introduction:v1'
const publicRoute = '/slots/sunstone-eclipse'
type SpinRequest = { gameId: string; wagerPoints: number; useFreeSpin: boolean }

type EclipseCapture = {
  source: { source: string; seed: number; attempt: number; EngineVersion: string; baseScale: number }
  initialState: { slotsCreditsBalance: number; freeSpinsRemaining: number;
    freeSpinWagerPoints: number | null; freeSpinFeatureMode: string | null }
  steps: { response: SpinResult & { spinFeature: SunwheelFeature } }[]
}
const eclipseCapture = JSON.parse(readFileSync(
  new URL('../../tools/fixtures/slot-contracts/sunstone-eclipse/base-eclipse.json', import.meta.url), 'utf8',
)) as EclipseCapture
const capturedSpin = eclipseCapture.steps[0].response
const freeCapture = JSON.parse(readFileSync(
  new URL('../../tools/fixtures/slot-contracts/sunstone-eclipse/free-eclipse.json', import.meta.url), 'utf8',
)) as EclipseCapture

/** Exact naturally sampled engine/report response; no board fabrication or linear payout rescaling. */
function nativeEclipseResponse(request: SpinRequest, captured = capturedSpin) {
  if (request.wagerPoints !== captured.wagerPoints || request.gameId !== captured.gameId || request.useFreeSpin !== captured.isFreeSpin)
    throw new Error('The UI must select the genuine captured spin terms before using this fixture.')
  return structuredClone(captured)
}

async function selectCapturedWager(page: Page) {
  const changeBet = page.getByRole('button', { name: /^Change bet, currently / })
  await changeBet.click()
  const dialog = page.getByRole('dialog', { name: 'Choose bet', exact: true })
  const slider = dialog.getByRole('slider', { name: 'Bet amount', exact: true })
  await slider.focus()
  await slider.press('Home')
  for (let step = 0; step < capturedSpin.wagerPoints / 2 - 1; step++) await slider.press('ArrowRight')
  await expect(changeBet).toHaveAttribute('aria-label', 'Change bet, currently R25')
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await expect(changeBet).toBeFocused()
}

async function mockAccountAndGame(page: Page, capture = eclipseCapture) {
  const requests: SpinRequest[] = []
  const reviewAccount = { ...account, balances: { ...account.balances,
    slotsCredits: capture.initialState.slotsCreditsBalance } }
  await page.route('**/api/**', route => route.fulfill({ status: 404, json: { error: 'No fixture for this API route.' } }))
  await page.route('**/api/games/experience', route => route.fulfill({ status: 401 }))
  await page.route('**/api/payments/invoices?**', route => route.fulfill({ json: { invoices: [] } }))
  await page.route('**/api/accounts/me', route => route.fulfill({ json: reviewAccount }))
  await page.route('**/api/catalog/visibility', route => route.fulfill({ json: {
    version: 1, games: [{ id: 'robot-revolution', route: publicRoute, visible: true }],
  } }))
  await page.route('**/api/games/activity/presence', route => route.fulfill({ status: 204 }))
  await page.route('**/api/slots/status**', route => route.fulfill({ status: 204 }))
  await page.route('**/api/slots/state?**', route => route.fulfill({ json: {
    ...capture.initialState, specialPointsBalance: 0, energyBalance: 0, sealCollections: [],
  } }))
  await page.route('**/api/slots/spins', async route => {
    const request = route.request().postDataJSON() as SpinRequest
    requests.push(request)
    await route.fulfill({ json: nativeEclipseResponse(request, capture.steps[0].response) })
  })
  return requests
}

async function ready(page: Page) {
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
  await page.waitForFunction(() => [...document.querySelectorAll<HTMLImageElement>('.slot-symbol__image, .sunwheel img')]
    .every(image => image.complete && image.naturalWidth > 0))
  await page.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>(done => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
}

async function expectNoHorizontalOverflow(page: Page, width: number) {
  const metrics = await page.evaluate(() => ({ viewport: innerWidth,
    document: document.documentElement.scrollWidth, body: document.body.scrollWidth }))
  expect(metrics, JSON.stringify(metrics)).toEqual({ viewport: width, document: width, body: width })
}

async function expectUsableHookAndControls(page: Page, viewport: { width: number; height: number }, name: string) {
  const metrics = await page.evaluate(() => {
    const bounds = (selector: string) => {
      const element = document.querySelector<HTMLElement>(selector)!
      const box = element.getBoundingClientRect()
      return { left: box.left, right: box.right, top: box.top, bottom: box.bottom,
        width: box.width, height: box.height, overflowX: element.scrollWidth - element.clientWidth }
    }
    const footer = document.querySelector('.slots-page__keyboard-help')?.getBoundingClientRect()
    const navbar = document.querySelector('.in-game-navbar')?.getBoundingClientRect()
    let clipTop = 0
    let clipBottom = innerHeight
    let ancestor = document.querySelector('.sunwheel')!.parentElement
    const clippingAncestors = []
    while (ancestor) {
      const style = getComputedStyle(ancestor)
      if (/hidden|clip|auto|scroll/.test(style.overflowY) || style.contain.includes('paint')) {
        const box = ancestor.getBoundingClientRect()
        clipTop = Math.max(clipTop, box.top)
        clipBottom = Math.min(clipBottom, box.bottom)
        clippingAncestors.push({ className: ancestor.className, top: box.top, bottom: box.bottom })
      }
      ancestor = ancestor.parentElement
    }
    return { viewport: { width: innerWidth, height: innerHeight },
      usableTop: Math.max(navbar?.bottom ?? 0, clipTop),
      usableBottom: Math.min(footer && footer.height > 0 && footer.width > 0 ? footer.top : innerHeight, clipBottom), clippingAncestors,
      hook: bounds('.sunwheel'), result: bounds('.sunwheel__result'), spin: bounds('.spin-button'),
      guardians: [...document.querySelectorAll<HTMLElement>('.sunwheel-guardian')].map(element => {
        const box = element.getBoundingClientRect()
        return { top: box.top, bottom: box.bottom, left: box.left, right: box.right }
      }),
      utilityButtons: ['How to win', 'Symbol values', 'Open settings'].map(name => {
        const button = [...document.querySelectorAll<HTMLButtonElement>('button')]
          .find(element => element.getAttribute('aria-label') === name || element.textContent?.trim() === name)!
        const box = button.getBoundingClientRect()
        return { name, top: box.top, bottom: box.bottom, left: box.left, right: box.right }
      }) }
  })
  await writeFile(resolve(screenshotDirectory, `${name}-layout.json`), JSON.stringify(metrics, null, 2))
  expect(metrics.hook.top, 'Sunwheel starts below the game navbar').toBeGreaterThanOrEqual(metrics.usableTop - 1)
  expect(metrics.hook.bottom, 'Complete Sunwheel must fit above the fixed footer').toBeLessThanOrEqual(metrics.usableBottom + 1)
  expect(metrics.result.bottom, 'Sunwheel award strip must be accessible').toBeLessThanOrEqual(metrics.usableBottom + 1)
  expect(metrics.guardians).toHaveLength(3)
  for (const guardian of metrics.guardians) {
    expect(guardian.top).toBeGreaterThanOrEqual(metrics.usableTop - 1)
    expect(guardian.bottom, 'Every guardian row must fit in the usable viewport').toBeLessThanOrEqual(metrics.usableBottom + 1)
    expect(guardian.left).toBeGreaterThanOrEqual(0)
    expect(guardian.right).toBeLessThanOrEqual(viewport.width)
  }
  expect(metrics.spin.top).toBeGreaterThanOrEqual(metrics.usableTop - 1)
  expect(metrics.spin.bottom).toBeLessThanOrEqual(viewport.height)
  expect(metrics.spin.left).toBeGreaterThanOrEqual(0)
  expect(metrics.spin.right).toBeLessThanOrEqual(viewport.width)
  for (const [index, button] of metrics.utilityButtons.entries()) {
    expect(button.left).toBeGreaterThanOrEqual(-1)
    expect(button.right).toBeLessThanOrEqual(viewport.width + 1)
    expect(button.top).toBeGreaterThanOrEqual(0)
    expect(button.bottom).toBeLessThanOrEqual(viewport.height)
    for (const other of metrics.utilityButtons.slice(index + 1)) {
      const overlap = Math.max(0, Math.min(button.right, other.right) - Math.max(button.left, other.left)) *
        Math.max(0, Math.min(button.bottom, other.bottom) - Math.max(button.top, other.top))
      expect(overlap, `${button.name} must not cover ${other.name}`).toBe(0)
    }
  }
}

for (const viewport of [
  { name: 'desktop', width: 1366, height: 900, mobile: false },
  { name: 'mobile', width: 390, height: 844, mobile: true },
  { name: 'compact', width: 320, height: 740, mobile: true },
]) {
  test.describe(viewport.name, () => {
    test.use({ viewport: { width: viewport.width, height: viewport.height }, hasTouch: viewport.mobile, isMobile: viewport.mobile })

    test('settled Sunwheel awards, guardian paths, illustrated rules and responsive artwork', async ({ page }) => {
      const errors: string[] = []
      page.on('pageerror', error => errors.push(error.message))
      const requests = await mockAccountAndGame(page)
      await mkdir(screenshotDirectory, { recursive: true })
      await page.goto(publicRoute)

      const guide = page.getByRole('dialog', { name: 'Welcome to Sunstone Eclipse', exact: true })
      await expect(guide).toBeVisible()
      await expect(guide.getByRole('button', { name: 'Close win guide' })).toBeFocused()
      await expect(guide.getByRole('heading', { name: 'Reveal the Eclipse' })).toBeAttached()
      await expect(guide).toContainText(/0\.5(?:0)?×/)
      await expect(guide).toContainText('20×')
      await expectNoHorizontalOverflow(page, viewport.width)
      await page.screenshot({ path: resolve(screenshotDirectory, `${viewport.name}-help.png`) })
      await page.keyboard.press('Tab')
      await expect(guide.getByRole('button', { name: 'Enter the Temple' })).toBeFocused()
      await page.keyboard.press('Enter')
      await expect(guide).toHaveCount(0)

      await ready(page)
      await expect(page.locator('.slots-page')).not.toContainText(/Robot Revolution|Aztec|quantum|fusion/i)
      await expect(page.locator('.sunwheel')).toContainText('Three guardians. One alignment.')
      await expectNoHorizontalOverflow(page, viewport.width)
      await page.screenshot({ path: resolve(screenshotDirectory, `${viewport.name}.png`), fullPage: true })
      await expectUsableHookAndControls(page, viewport, `${viewport.name}-idle`)

      await selectCapturedWager(page)
      await page.getByRole('button', { name: 'Spin the reels', exact: true }).click()
      const wheel = page.locator('.sunwheel')
      await expect(wheel).toHaveAttribute('data-eclipse', 'true')
      await expect(wheel.locator('.sunwheel-guardian[data-aligned="true"]')).toHaveCount(3)
      await expect(wheel.locator('.sunwheel__result')).toContainText('Eclipse tribute')
      await expect(wheel.locator('.sunwheel__result')).not.toContainText('details are unavailable')
      expect(requests).toHaveLength(1)
      expect(requests[0].gameId).toBe('robot-revolution-v1')
      const fixture = nativeEclipseResponse(requests[0])
      const nativeAward = new Intl.NumberFormat('en-US')
        .format(fixture.spinFeature.totalAwardPoints * fixture.pointValueInCents / 100)
      await expect(wheel.getByLabel('Sunwheel award', { exact: true }))
        .toHaveText(`+R${nativeAward}`)
      await expect(page.locator('.slot-winning-paylines')).toHaveCount(0)
      if (viewport.mobile) {
        await expect(page.locator('.slots-page__footer')).toContainText('Feature win')
        await expect(page.locator('.slots-page__footer')).not.toContainText('Winning symbols are highlighted')
      }
      await expectNoHorizontalOverflow(page, viewport.width)
      await expect(wheel).toBeInViewport({ ratio: 0.9 })
      await page.screenshot({ path: resolve(screenshotDirectory, `${viewport.name}-eclipse.png`), fullPage: true })
      await expectUsableHookAndControls(page, viewport, `${viewport.name}-settled`)

      const jaguar = wheel.getByRole('button', {
        name: `Show Jaguar path, ${fixture.spinFeature.rings[0].matches} of five positions matched`,
      })
      await jaguar.focus()
      await expect(jaguar).toHaveAttribute('aria-pressed', 'true')
      const path = page.locator('.sunwheel-board-path')
      await expect(path).toBeVisible()
      await expect(path.locator('circle[data-matched="true"]')).toHaveCount(fixture.spinFeature.rings[0].matches)
      await expect(path.locator('polyline')).toHaveAttribute('points', /\d/)
      await page.screenshot({ path: resolve(screenshotDirectory, `${viewport.name}-guardian.png`), fullPage: true })

      const help = page.getByRole('button', { name: 'How to win', exact: true })
      await help.click({ timeout: 5_000 })
      await expect(guide).toBeVisible()
      await page.keyboard.press('Escape')
      await expect(guide).toHaveCount(0)
      await expect(help).toBeFocused()
      if (viewport.mobile) {
        const values = page.getByRole('button', { name: 'Symbol values', exact: true })
        await values.click({ timeout: 5_000 })
        const valuesDialog = page.getByRole('dialog', { name: 'Symbol values', exact: true })
        await expect(valuesDialog).toBeVisible()
        await valuesDialog.getByRole('button', { name: 'Close symbol values', exact: true }).click({ timeout: 5_000 })
        await expect(valuesDialog).toHaveCount(0)
        await expect(values).toBeFocused()
      }
      await expectNoHorizontalOverflow(page, viewport.width)
      expect(errors).toEqual([])
      await writeFile(resolve(screenshotDirectory, `${viewport.name}-state.json`), JSON.stringify({
        fixtureSource: eclipseCapture.source,
        viewport, requests, feature: fixture.spinFeature, pageErrors: errors,
      }, null, 2))
    })
  })
}

test('the legacy Robot Revolution route opens the replacement without losing its game identity', async ({ page }) => {
  await mockAccountAndGame(page)
  await page.addInitScript(key => localStorage.setItem(key, '1'), introductionKey)
  await page.goto('/slots/robot-revolution')
  await expect(page).toHaveURL(/\/slots\/sunstone-eclipse$/)
  await ready(page)
  await expect(page.locator('.slots-page')).toContainText('Sunstone')
  await expect(page.locator('.sunwheel')).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test.describe('expanded Eclipse on compact phone', () => {
  test.use({ viewport: { width: 320, height: 740 }, hasTouch: true, isMobile: true })

  test('six-row locked-wager bonus keeps every guardian and spin control accessible', async ({ page }) => {
    const requests = await mockAccountAndGame(page, freeCapture)
    await page.addInitScript(key => localStorage.setItem(key, '1'), introductionKey)
    await page.goto(publicRoute)
    await ready(page)
    await page.getByRole('button', { name: 'Spin the reels', exact: true }).click()
    const wheel = page.locator('.sunwheel')
    await expect(wheel).toHaveAttribute('data-active-round', 'true')
    await expect(wheel.locator('.sunwheel__mode')).toHaveText('Eclipse spins · double wheel awards')
    await expect(page.locator('.slot-symbol[data-reel-index][data-row-index]')).toHaveCount(30)
    expect(requests).toHaveLength(1)
    expect(requests[0]).toMatchObject({ gameId: 'robot-revolution-v1', wagerPoints: 100, useFreeSpin: true })
    const response = freeCapture.steps[0].response
    const amount = new Intl.NumberFormat('en-US').format(response.spinFeature.totalAwardPoints * response.pointValueInCents / 100)
    await expect(wheel.getByLabel('Sunwheel award', { exact: true }))
      .toHaveText(response.spinFeature.totalAwardPoints > 0 ? `+R${amount}` : 'SUNWHEEL')
    await expect(page.locator('.slot-winning-paylines [data-payline-id="1001"], .slot-winning-paylines [data-payline-id="1002"], .slot-winning-paylines [data-payline-id="1003"], .slot-winning-paylines [data-payline-id="1010"]')).toHaveCount(0)
    await expectNoHorizontalOverflow(page, 320)
    await page.screenshot({ path: resolve(screenshotDirectory, 'compact-expanded.png'), fullPage: true })
    await expectUsableHookAndControls(page, { width: 320, height: 740 }, 'compact-expanded')
  })
})

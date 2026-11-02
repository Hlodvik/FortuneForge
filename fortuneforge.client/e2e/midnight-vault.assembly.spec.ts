import { expect, test, type Page } from '@playwright/test'
import { readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { midnightVaultMultiplier } from '../src/games/slots/dragonHoard/math'

type EngineResponse = {
  gameId: string; wagerPoints: number; pointValueInCents: number; isFreeSpin: boolean
  freeSpinsAwarded: number; freeSpinsRemaining: number; freeSpinFeatureMode: string | null
  reels: string[][]; payout: { totalPoints: number }
}
type EngineFixture = { scenario: string; source: unknown; wagerPoints: number; steps: { request: unknown; response: EngineResponse }[] }

const evidenceRoot = resolve('../tools/fixtures/midnight-vault')
const formatRand = (amount: number) => `R${new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)}`

async function ready(page: Page) {
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
  await page.waitForFunction(() => {
    const images = [...document.querySelectorAll<HTMLImageElement>('.slot-symbol__image')]
    return images.length > 0 && images.every(image => image.currentSrc && image.complete && image.naturalWidth > 0)
  })
}

async function imageBounds(page: Page) {
  const state = await page.locator('.slot-symbol__image').evaluateAll(images => {
    const frame = document.querySelector('.slot-machine__reels')!.getBoundingClientRect()
    return images.map(node => {
      const image = node as HTMLImageElement
      const box = image.getBoundingClientRect()
      return { currentSrc: new URL(image.currentSrc).pathname, sizes: image.sizes, width: box.width, height: box.height,
        box: { left: box.left, right: box.right, top: box.top, bottom: box.bottom },
        frame: { left: frame.left, right: frame.right, top: frame.top, bottom: frame.bottom },
        clipped: box.left < frame.left - 1 || box.right > frame.right + 1 || box.top < frame.top - 1 || box.bottom > frame.bottom + 1 }
    })
  })
  expect(state.filter(image => image.clipped || image.width <= 0 || image.height <= 0)).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  return state
}

test.beforeEach(async ({ baseURL }) => {
  expect(process.env.MIDNIGHT_LOCAL_API).toBe('1')
  expect(new URL(baseURL!).hostname).toBe('127.0.0.1')
})

for (const scenario of ['base-loss', 'base-win', 'scatter', 'collection-sync', 'collection-rows', 'collection-paw', 'collection-rand']) {
  test(`Midnight Vault real engine presentation: ${scenario}`, async ({ page }, testInfo) => {
    test.setTimeout(180_000)
    const fixture = JSON.parse(await readFile(resolve(evidenceRoot, `${scenario}.json`), 'utf8')) as EngineFixture
    const errors: string[] = []
    const requested: string[] = []
    const records: unknown[] = []
    page.on('pageerror', error => errors.push(error.message))
    page.on('request', request => requested.push(request.url()))
    await page.setViewportSize(scenario === 'collection-rand' ? { width: 390, height: 844 } : { width: 1366, height: 900 })
    await page.goto(`/slots/dragon-hoard/demo?review=${scenario}`)
    await ready(page)
    await expect(page.getByRole('heading', { name: 'Midnight Vault', exact: true })).toHaveCount(1)
    let balance = 10_000
    if (scenario === 'base-loss') {
      await expect(page.locator('.midnight-vault__wordmark img')).toBeVisible()
      const desktopStage = await page.locator('.slots-page__stage').boundingBox()
      expect(desktopStage!.width).toBeLessThanOrEqual(1366 * .85)
      expect(desktopStage!.x).toBeGreaterThan(1366 * .07)
      await expect(page.locator('[data-collection-presentation="vault-access"]')).toHaveCount(4)
      records.push({ state: 'initial', images: await imageBounds(page) })
      await page.screenshot({ path: testInfo.outputPath('desktop-base.png') })
      await page.setViewportSize({ width: 390, height: 844 })
      await ready(page)
      await expect(page.locator('.midnight-vault__wordmark img')).toBeVisible()
      expect(await page.locator('.slot-game-frame').evaluate(node => node.getBoundingClientRect().height)).toBeGreaterThan(330)
      const consoleBox = await page.locator('.slots-page__playbar').boundingBox()
      expect(consoleBox!.y + consoleBox!.height).toBeLessThanOrEqual(844)
      await imageBounds(page)
      await page.screenshot({ path: testInfo.outputPath('phone-base.png') })
      await page.setViewportSize({ width: 1366, height: 900 })
      await page.evaluate(async () => { await document.documentElement.requestFullscreen() })
      expect(await page.evaluate(() => Boolean(document.fullscreenElement))).toBe(true)
      await ready(page)
      records.push({ state: 'fullscreen', images: await imageBounds(page) })
      await page.screenshot({ path: testInfo.outputPath('fullscreen-base.png') })
      await page.evaluate(async () => { await document.exitFullscreen() })
      await page.setViewportSize({ width: 900, height: 600 })
      await ready(page)
      const compactConsole = await page.locator('.slots-page__playbar').boundingBox()
      expect(compactConsole!.y + compactConsole!.height).toBeLessThanOrEqual(600)
      records.push({ state: 'compact-desktop', images: await imageBounds(page) })
      await page.screenshot({ path: testInfo.outputPath('compact-desktop-base.png') })
      await page.setViewportSize({ width: 1366, height: 900 })
      await ready(page)
    }
    for (const [index, step] of fixture.steps.entries()) {
      const incoming = page.waitForResponse(response => response.url().endsWith('/api/slots/demo/spins') && response.request().method() === 'POST')
      await page.getByRole('button', { name: 'Spin the reels', exact: true }).click()
      const response = await incoming
      expect(response.status()).toBe(200)
      const request = response.request().postDataJSON()
      expect(request).toMatchObject({ gameId: 'dragon-hoard-v1', wagerPoints: fixture.wagerPoints, useFreeSpin: index > 0 })
      const result = await response.json() as EngineResponse
      expect(result).toEqual(step.response)
      balance += (result.payout.totalPoints - (result.isFreeSpin ? 0 : result.wagerPoints)) * result.pointValueInCents / 100
      await ready(page)
      await expect(page.locator('.slots-page__balance-value')).toHaveText(formatRand(balance))
      await expect(page.locator('.slot-symbol')).toHaveCount(result.reels.flat().length)
      if (result.freeSpinsRemaining > 0) {
        await expect(page.locator('.midnight-vault__bonus-hud strong')).toHaveText(`${result.freeSpinsRemaining} bonus ${result.freeSpinsRemaining === 1 ? 'spin' : 'spins'} left`)
        await expect(page.getByRole('button', { name: 'Increase wager' })).toHaveCount(0)
        await expect(page.getByRole('button', { name: 'Start autospin' })).toHaveCount(0)
        await expect(page.locator('.midnight-vault__locked-wager')).toContainText(formatRand(result.wagerPoints * result.pointValueInCents / 100))
      }
      if (index === 0 || index === 1 || index === fixture.steps.length - 1) {
        records.push({ index, request, response: result, balance, images: await imageBounds(page) })
        await page.screenshot({ path: testInfo.outputPath(`${scenario}-step-${index}.png`) })
      }
      if (index > 0 && result.freeSpinFeatureMode === 'rows-paw') expect(result.reels.every(reel => reel.length === 6)).toBe(true)
      if (scenario === 'scatter' && index > 0) expect(result.reels.every(reel => reel.length === 4)).toBe(true)
    }
    if (fixture.steps.length > 1) {
      await expect(page.locator('.midnight-vault__bonus-hud')).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Increase wager' })).toBeVisible()
      // Return from the expanded six-row feature to the ordinary base window.
      if (scenario.startsWith('collection-')) {
        const incoming = page.waitForResponse(response => response.url().endsWith('/api/slots/demo/spins'))
        await page.getByRole('button', { name: 'Spin the reels', exact: true }).click()
        await incoming
        await ready(page)
        await expect(page.locator('.slot-symbol')).toHaveCount(20)
      }
    }
    expect(errors).toEqual([])
    expect(requested.every(url => new URL(url).hostname === '127.0.0.1')).toBe(true)
    expect(requested.some(url => url.includes('/api/slots/spins'))).toBe(false)
    expect(requested.some(url => /\/games\/dragon-hoard\/|ancient-sword|cyber-night/.test(url))).toBe(false)
    await writeFile(testInfo.outputPath(`${scenario}-review.json`), JSON.stringify({ scenario, source: fixture.source, records, requested, errors }, null, 2))
  })
}

test('Midnight Vault catalogue, loading, guides and audio preferences', async ({ page }, testInfo) => {
  test.setTimeout(90_000)
  await page.setViewportSize({ width: 1366, height: 900 })
  await page.goto('/demo')
  const card = page.locator('article.machine-card').filter({ has: page.getByRole('link', { name: 'Play demo: Midnight Vault', exact: true }) })
  await card.scrollIntoViewIfNeeded()
  await expect(card.locator('strong')).toHaveText('Midnight Vault')
  await expect(card.locator('img')).toHaveAttribute('srcset', /midnight-vault-catalogue-title/)
  await card.screenshot({ path: testInfo.outputPath('catalogue-card.png') })
  await page.goto('/slots/dragon-hoard/demo?review=base-win&reviewDelayMs=1800')
  const loading = page.locator('[data-game-loading="dragon-hoard"]')
  await expect(loading).toBeVisible()
  await expect(loading.getByRole('heading')).toHaveText('Midnight Vault')
  await expect(loading.locator('img')).toHaveJSProperty('complete', true)
  await page.screenshot({ path: testInfo.outputPath('loading-screen.png') })
  await ready(page)
  await page.getByRole('button', { name: 'How to win', exact: true }).click()
  const guide = page.getByRole('dialog', { name: 'What counts as a win?' })
  await expect(guide).toContainText('23 full payline patterns')
  await expect(guide).toContainText('6 Vault Breach spins')
  await expect(guide).toContainText('7 enhanced Vault Breach spins')
  await expect(guide).not.toContainText('gathers every')
  await page.screenshot({ path: testInfo.outputPath('win-guide.png') })
  await page.getByRole('button', { name: 'Close win guide' }).click()
  await page.getByRole('button', { name: 'Symbol values', exact: true }).click()
  const values = page.getByRole('dialog', { name: 'Symbol values', exact: true })
  await expect(values).toContainText(midnightVaultMultiplier(1))
  await expect(values.locator('.symbol-value-guide__item--bolt')).toHaveCount(0)
  await expect(values.locator('.symbol-value-guide__item--paw')).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('symbol-values.png') })
  await page.getByRole('button', { name: 'Close symbol values' }).click()
  await page.getByRole('button', { name: 'Open settings' }).click()
  await page.getByRole('button', { name: 'Mute Turn off all game audio.' }).click()
  await expect(page.getByRole('button', { name: 'Mute Turn off all game audio.' })).toHaveAttribute('aria-pressed', 'true')
  await page.getByRole('button', { name: 'Close settings' }).click()
  await page.reload()
  await ready(page)
  await page.getByRole('button', { name: 'Open settings' }).click()
  await expect(page.getByRole('button', { name: 'Mute Turn off all game audio.' })).toHaveAttribute('aria-pressed', 'true')
  await page.screenshot({ path: testInfo.outputPath('audio-preferences.png') })
})

test('Midnight Vault jewel celebration uses geometric glints', async ({ page }, testInfo) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await page.goto('/slots/dragon-hoard/demo?review=base-win')
  await ready(page)
  await page.getByRole('button', { name: 'Spin the reels', exact: true }).click()
  const effect = page.locator('.slots-page__result-atmosphere[data-celebration="vault-jewel-glint"]')
  await expect(effect).toBeAttached()
  const content = await effect.evaluate(node => getComputedStyle(node, '::before').content)
  expect(content).toContain('◇')
  expect(content).not.toMatch(/🐉|🔥/)
  await page.screenshot({ path: testInfo.outputPath('jewel-celebration.png') })
})

test('Midnight Vault keeps a local service error visible above the cabinet', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/slots/dragon-hoard/demo?review=unavailable-fixture')
  await ready(page)
  await page.getByRole('button', { name: 'Spin the reels', exact: true }).click()
  const message = page.locator('.slots-page__footer')
  await expect(message).toContainText('The game service is offline')
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeDisabled()
  const bounds = await message.boundingBox()
  expect(bounds!.y).toBeGreaterThanOrEqual(0)
  expect(bounds!.y + bounds!.height).toBeLessThan(844)
  await expect(page.locator('.slots-page__balance-value')).toHaveText('R10,000.00')
  await page.screenshot({ path: testInfo.outputPath('service-error.png') })
})

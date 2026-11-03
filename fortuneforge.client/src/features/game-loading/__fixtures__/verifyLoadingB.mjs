import assert from 'node:assert/strict'
import { mkdir, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium } from 'playwright'

// Run against a local Vite server. Captures are review output, not runtime assets.
const base = process.env.LOADING_REVIEW_URL ?? 'http://127.0.0.1:4187'
const output = process.env.LOADING_REVIEW_OUTPUT
const ids = [
  'dragon-tiger', 'andar-bahar', 'hi-lo', 'casino-war', 'video-poker', 'keno', 'sic-bo',
  'arcane-archives', 'cosmic-fortune', 'gold-foundry', 'neon-nights',
  'jungle-jackpot', 'ocean-odyssey', 'samurai-fortune',
]
const fixture = `${base}/src/features/game-loading/__fixtures__/loadingB.html`
const browser = await chromium.launch({ headless: true, channel: process.env.LOADING_REVIEW_BROWSER ?? 'chrome' })
const page = await browser.newPage()
const requests = new Set()
const errors = []
page.on('request', (request) => requests.add(request.url()))
page.on('pageerror', (error) => errors.push(error.message))
const results = []

try {
  if (output) await mkdir(output, { recursive: true })
  for (const [name, viewport] of [
    ['desktop', { width: 1440, height: 900 }],
    ['phone', { width: 375, height: 812 }],
  ]) {
    await page.setViewportSize(viewport)
    for (const id of ids) {
      await page.goto(`${fixture}?game=${id}`, { waitUntil: 'networkidle' })
      await page.locator('img').evaluate((image) => image.decode())
      const metrics = await page.evaluate(() => {
        const image = document.querySelector('img')
        const bounds = image.getBoundingClientRect()
        const root = document.querySelector('.ff-game-loading')
        return {
          title: document.querySelector('h1').textContent,
          status: document.querySelector('[role="status"]').textContent,
          width: image.naturalWidth, height: image.naturalHeight,
          fit: getComputedStyle(image).objectFit,
          imageWidth: bounds.width, imageHeight: bounds.height,
          overflowX: document.documentElement.scrollWidth > innerWidth,
          overflowY: document.documentElement.scrollHeight > innerHeight,
          background: getComputedStyle(root).backgroundColor,
          controls: document.querySelectorAll('button,a,input,audio,video,progress,[aria-valuenow]').length,
        }
      })
      assert.equal(metrics.width, 768, `${name}/${id}: artwork did not load`)
      assert.equal(metrics.height, 384)
      assert.equal(metrics.fit, 'contain', `${name}/${id}: artwork must not be cropped`)
      assert.equal(metrics.overflowX, false, `${name}/${id}: horizontal overflow`)
      assert.equal(metrics.overflowY, false, `${name}/${id}: vertical overflow`)
      assert.equal(metrics.controls, 0)
      assert.equal(metrics.status, `Loading ${metrics.title}…`)
      assert(Math.abs(metrics.imageWidth / metrics.imageHeight - 2) < 0.03)
      results.push({ viewport: name, id, ...metrics })
      if (output) await page.screenshot({ path: join(output, `${name}-${id}.png`) })
    }
  }

  for (const viewport of [
    { width: 320, height: 568 }, { width: 844, height: 390 },
    { width: 1366, height: 600 }, { width: 1366, height: 720 },
  ]) {
    await page.setViewportSize(viewport)
    for (const shell of ['', '&shell=1']) {
      await page.goto(`${fixture}?game=jungle-jackpot${shell}`, { waitUntil: 'networkidle' })
      const bounds = await page.evaluate(() => {
        const host = document.querySelector('.in-game-shell__body')?.getBoundingClientRect()
        const content = document.querySelector('.ff-game-loading__content').getBoundingClientRect()
        const status = document.querySelector('[role="status"]').getBoundingClientRect()
        return {
          width: document.documentElement.scrollWidth,
          height: document.documentElement.scrollHeight,
          top: host?.top ?? 0, bottom: host?.bottom ?? innerHeight,
          contentTop: content.top, contentBottom: content.bottom, statusBottom: status.bottom,
        }
      })
      assert(bounds.width <= viewport.width, `${viewport.width}x${viewport.height}${shell}: horizontal overflow`)
      assert(bounds.height <= viewport.height, `${viewport.width}x${viewport.height}${shell}: vertical overflow`)
      assert(bounds.contentTop >= bounds.top - 1, `${viewport.width}x${viewport.height}${shell}: artwork clipped above shell`)
      assert(bounds.contentBottom <= bounds.bottom + 1, `${viewport.width}x${viewport.height}${shell}: content clipped below shell`)
      assert(bounds.statusBottom <= bounds.bottom + 1, `${viewport.width}x${viewport.height}${shell}: status clipped below shell`)
    }
  }

  await page.setViewportSize({ width: 375, height: 812 })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto(`${fixture}?game=dragon-tiger`, { waitUntil: 'networkidle' })
  assert.equal(await page.locator('.ff-game-loading__indicator').evaluate((node) => getComputedStyle(node).animationName), 'none')
  await page.goto(`${fixture}?game=dragon-tiger&missing=1`, { waitUntil: 'networkidle' })
  assert.equal(await page.locator('img').count(), 0)
  assert.equal(await page.locator('.ff-game-loading__fallback-title').textContent(), 'Dragon Tiger')
  if (output) await page.screenshot({ path: join(output, 'phone-missing-art.png') })
  await page.goto(fixture, { waitUntil: 'networkidle' })
  assert.equal(await page.locator('img').count(), 0)
  assert.equal(await page.locator('[role="status"]').textContent(), 'Loading game…')

  assert.equal(errors.length, 0, errors.join('\n'))
  assert.equal([...requests].some((url) => /\/src\/games\/|@fortuneforge/.test(url)), false,
    'Loading presentations must not fetch game-package modules')
  if (output) await writeFile(join(output, 'verification.json'), JSON.stringify(results, null, 2))
  console.log(JSON.stringify({ checkedPresentations: ids.length, viewportChecks: results.length,
    edgeViewportAndShellChecks: 8, reducedMotion: 'passed', genericAndFailedArt: 'passed',
    gamePackageRequests: 0, pageErrors: errors, output }))
} finally {
  await browser.close()
}

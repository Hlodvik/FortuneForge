import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdir, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { chromium } from 'playwright'

// Run against a local Vite server. Review captures are optional and never runtime assets.
const base = process.env.LOADING_REVIEW_URL ?? 'http://127.0.0.1:4186'
const output = process.env.LOADING_REVIEW_OUTPUT
const ids = [
  'blackjack', 'texas-holdem', 'three-card-poker', 'house-holdem', 'baccarat', 'roulette', 'craps',
  'wukong-journey-to-the-west', 'rainbow-realm', 'pirates-fortune', 'gods-of-olympus',
  'reel-riches', 'high-noon-fortune', 'royal-draw',
]
const fixture = `${base}/src/features/game-loading/__fixtures__/loadingA.html`
const browser = await chromium.launch({ headless: true, channel: process.env.LOADING_REVIEW_BROWSER ?? 'chrome' })
const page = await browser.newPage()
const requests = new Set()
const errors = []
page.on('request', (request) => requests.add(request.url()))
page.on('pageerror', (error) => errors.push(error.message))
const results = []
const captures = { desktop: [], phone: [] }

try {
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
        const root = document.querySelector('.ff-game-loading')
        const bounds = image.getBoundingClientRect()
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
      if (output) captures[name].push(await page.locator('.ff-game-loading__content').screenshot())
    }
  }

  for (const viewport of [
    { width: 320, height: 568 }, { width: 844, height: 390 },
    { width: 1366, height: 600 }, { width: 1366, height: 720 },
  ]) {
    await page.setViewportSize(viewport)
    for (const shell of ['', '&shell=1']) {
      await page.goto(`${fixture}?game=wukong-journey-to-the-west${shell}`, { waitUntil: 'networkidle' })
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
      assert.equal(await page.evaluate(() => document.documentElement.scrollHeight > innerHeight), false,
        `${viewport.width}x${viewport.height}${shell}: shell overflow`)
      const bounds = await page.evaluate(() => {
        const host = document.querySelector('.in-game-shell__body')?.getBoundingClientRect()
        const content = document.querySelector('.ff-game-loading__content').getBoundingClientRect()
        const status = document.querySelector('[role="status"]').getBoundingClientRect()
        return { top: host?.top ?? 0, bottom: host?.bottom ?? innerHeight,
          contentTop: content.top, contentBottom: content.bottom, statusBottom: status.bottom }
      })
      assert(bounds.contentTop >= bounds.top - 1, `${viewport.width}x${viewport.height}${shell}: artwork clipped above shell`)
      assert(bounds.contentBottom <= bounds.bottom + 1, `${viewport.width}x${viewport.height}${shell}: content clipped below shell`)
      assert(bounds.statusBottom <= bounds.bottom + 1, `${viewport.width}x${viewport.height}${shell}: status clipped below shell`)
    }
  }

  await page.setViewportSize({ width: 375, height: 812 })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto(`${fixture}?game=blackjack`, { waitUntil: 'networkidle' })
  assert.equal(await page.locator('.ff-game-loading__indicator').evaluate((node) => getComputedStyle(node).animationName), 'none')
  if (output) {
    await mkdir(output, { recursive: true })
    await page.screenshot({ path: join(output, 'phone-blackjack.png') })
  }
  await page.goto(`${fixture}?game=blackjack&missing=1`, { waitUntil: 'networkidle' })
  assert.equal(await page.locator('img').count(), 0)
  assert.equal(await page.locator('h1').textContent(), 'Fortune Blackjack')
  assert.equal(await page.locator('.ff-game-loading__fallback-title').isVisible(), true)
  if (output) await page.screenshot({ path: join(output, 'phone-missing-art.png') })
  await page.goto(fixture, { waitUntil: 'networkidle' })
  assert.equal(await page.locator('img').count(), 0)
  assert.equal(await page.locator('[role="status"]').textContent(), 'Loading game…')
  if (output) await page.screenshot({ path: join(output, 'phone-generic.png') })

  assert.equal(errors.length, 0, errors.join('\n'))
  assert.equal([...requests].some((url) => /\/src\/games\/|@fortuneforge/.test(url)), false,
    'Loading presentations must not fetch game-package modules')

  if (output) {
    const runtime = createRequire('C:/Users/Lctiv/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/')
    const sharp = runtime('sharp')
    for (const [name, shots] of Object.entries(captures)) {
      const width = name === 'desktop' ? 480 : 375
      const height = name === 'desktop' ? 300 : 260
      for (let start = 0; start < shots.length; start += 7) {
        const group = shots.slice(start, start + 7)
        const tiles = await Promise.all(group.map((shot) => sharp(shot).resize(width, height,
          { fit: 'contain', background: '#100c19' }).png().toBuffer()))
        await sharp({ create: { width: width * 2, height: height * 4, channels: 3, background: '#100c19' } })
          .composite(tiles.map((input, index) => ({ input, left: (index % 2) * width, top: Math.floor(index / 2) * height })))
          .png().toFile(join(output, `${name}-${start === 0 ? 'table-games' : 'slots'}.png`))
      }
    }
    await writeFile(join(output, 'verification.json'), JSON.stringify(results, null, 2))
  }
  console.log(JSON.stringify({ checkedPresentations: ids.length, viewportChecks: results.length,
    edgeViewportAndShellChecks: 8, reducedMotion: 'passed', genericAndFailedArt: 'passed',
    gamePackageRequests: 0, pageErrors: errors, output }))
} finally {
  await browser.close()
}

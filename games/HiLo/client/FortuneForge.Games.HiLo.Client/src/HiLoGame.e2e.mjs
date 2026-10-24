import assert from 'node:assert/strict'
import { mkdir, writeFile } from 'node:fs/promises'
import { chromium, expect } from '@playwright/test'

// Real standalone practice API; no client RNG, deterministic deal override, or production wallet.
const origin = process.env.HILO_URL || 'http://127.0.0.1:5183'
const output = new URL('../../../evidence/', import.meta.url)
await mkdir(output, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: process.env.HILO_BROWSER_CHANNEL || 'chrome' })
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, hasTouch: true })
const page = await context.newPage()
const errors = []
page.on('pageerror', error => errors.push(error.message))
const audio = []
await page.addInitScript(() => {
  window.__hiloAudio = []
  HTMLMediaElement.prototype.play = function () { window.__hiloAudio.push(this.src); return Promise.resolve() }
})
const api = path => `${origin}/api/games/hi-lo${path}`
const session = async () => { const response = await context.request.get(api('/session')); assert.equal(response.status(), 200); return response.json() }
const money = cents => `R${(cents / 100).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const start = () => page.getByRole('button', { name: 'Deal cards' })
const collect = () => page.getByRole('button', { name: /^Cash out/ })
const higher = () => page.getByRole('button', { name: /^Choose higher/ })
const lower = () => page.getByRole('button', { name: /^Choose lower/ })
const call = async (locator) => {
  const response = page.waitForResponse(r => r.request().method() === 'POST' && r.url().includes('/api/games/hi-lo/rounds'))
  await locator.click()
  const result = await response
  assert.equal(result.status(), 200)
  const next = await result.json()
  await expect(page.getByTestId('hilo-balance')).toHaveText(money(next.walletBalanceCents))
  await expect(page.getByRole('region', { name: 'Hi-Lo card game' })).toHaveAttribute('aria-busy', 'false')
  return next
}
const report = { origin, testedAt: new Date().toISOString(), viewports: [], flows: [] }
try {
  await page.goto(origin)
  await expect(start()).toBeEnabled()
  assert.equal((await session()).practice, true)
  const before = await session()
  await page.getByLabel('Wager amount').fill('2.00')
  let next = await call(start())
  assert.equal(next.walletBalanceCents, before.walletBalanceCents - 200)
  assert.equal(next.round.wagerCents, 200)
  assert.equal(next.round.currentReturnCents, 200)
  assert.equal(next.round.history.length, 1)
  report.flows.push('Wager debited once and initial server card shown')
  await page.reload()
  await expect(collect()).toBeEnabled()
  assert.deepEqual((await session()).round, next.round)
  await call(page.getByRole('button', { name: /^Skip card/ }))
  next = await session()
  assert.equal(next.round.skips, 1)
  assert.equal(next.round.currentReturnCents, 200)
  assert.equal(next.round.streak, 0)
  report.flows.push('Refresh resumes same active round; skip preserves return')

  // Exercise an actual winning streak with server randomness; use high-probability calls.
  let won = false
  for (let attempt = 0; attempt < 20 && !won; attempt++) {
    if (next.round.status !== 'active') next = await call(start())
    const prior = next
    const choice = prior.round.higher.probability >= prior.round.lower.probability ? 'higher' : 'lower'
    next = await call(choice === 'higher' ? higher() : lower())
    const expectedQuote = prior.round[choice]
    const values = ['ace','two','three','four','five','six','seven','eight','nine','ten','jack','queen','king']
    const previousRank = values.indexOf(prior.round.currentCard.rank)
    const newRank = values.indexOf(next.round.currentCard.rank)
    const correct = choice === 'higher' ? newRank > previousRank : newRank < previousRank
    assert.equal(next.round.status === 'active', correct)
    if (correct) {
      assert.equal(next.round.currentReturnCents, expectedQuote.projectedReturnCents)
      assert.equal(next.round.streak, prior.round.streak + 1)
      won = true
    }
  }
  assert.equal(won, true, 'Could not observe a win in twenty independent practice rounds')
  await page.screenshot({ path: new URL('desktop-active.png', output).pathname.replace(/^\/(\w:)/,'$1'), fullPage: true })

  for (const viewport of [
    { name: 'desktop', width: 1440, height: 900 },
    { name: 'phone-320', width: 320, height: 640 },
    { name: 'phone-360', width: 360, height: 740 },
    { name: 'phone-390', width: 390, height: 844 },
    { name: 'phone-430', width: 430, height: 932 },
    { name: 'landscape', width: 844, height: 390 },
  ]) {
    await page.setViewportSize(viewport)
    await page.evaluate(() => document.fonts.ready)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `${viewport.name} horizontal overflow`)
    assert.equal(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1), true, `${viewport.name} document scrolling`)
    const selectors = ['.hilo-choice-wrap--higher button', '.hilo-choice-wrap--lower button', '.hilo-collect', '.hilo-bet-controls .hilo-start', '.hilo-wager-input', '.hilo-utilities']
    for (const selector of selectors) {
      const box = await page.locator(selector).boundingBox()
      assert.ok(box && box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, `${viewport.name}: ${selector} clips`)
    }
    for (const selector of ['.hilo-choice','.hilo-collect','.hilo-bet-controls .hilo-start', '.hilo-skip']) {
      const box = await page.locator(selector).first().boundingBox()
      assert.ok(box.height >= 44 && box.width >= 44, `${viewport.name}: ${selector} touch target too small`)
    }
    await page.screenshot({ path: new URL(`${viewport.name}-active.png`, output).pathname.replace(/^\/(\w:)/,'$1'), fullPage: true })
    report.viewports.push(viewport)
  }
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: 'How to play Hi-Lo' }).tap()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: 'How to play Hi-Lo' })).toBeFocused()
  report.flows.push('Touch rules dialog and Escape focus return')

  // Lose a successful server cash-out response; reloading repeats its exact operation key.
  const cashBefore = await session()
  let accepted, command, mutationCount = 0
  await page.route('**/api/games/hi-lo/rounds/*/actions', async route => {
    mutationCount++
    command = route.request().postDataJSON()
    const response = await route.fetch()
    assert.equal(response.status(), 200)
    accepted = await response.json()
    await route.abort('failed')
  })
  await collect().tap()
  await expect(page.getByRole('button', { name: 'Restore action' })).toBeEnabled()
  assert.equal(accepted.walletBalanceCents, cashBefore.walletBalanceCents + cashBefore.round.currentReturnCents)
  assert.equal(mutationCount, 1)
  await page.unroute('**/api/games/hi-lo/rounds/*/actions')
  const retryResponse = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/actions'))
  await page.reload()
  const replay = await retryResponse
  assert.deepEqual(replay.request().postDataJSON(), command)
  assert.deepEqual(await replay.json(), accepted)
  await expect(start()).toBeEnabled()
  assert.equal((await session()).walletBalanceCents, accepted.walletBalanceCents)
  await expect(page.getByText('Added to your wallet')).toBeVisible()
  report.flows.push('Lost cash-out response + reload replays exact key, credits once')

  // Exact request conflict remains rejected without changing the wallet.
  const replayAgain = await context.request.post(api(`/rounds/${cashBefore.round.roundId}/actions`), { headers: { 'X-FortuneForge-Game': 'hi-lo' }, data: command })
  assert.equal(replayAgain.status(), 200)
  const conflict = await context.request.post(api(`/rounds/${cashBefore.round.roundId}/actions`), { headers: { 'X-FortuneForge-Game': 'hi-lo' }, data: { ...command, choice: 'skip' } })
  assert.equal(conflict.status(), 409)
  assert.equal((await session()).walletBalanceCents, accepted.walletBalanceCents)
  report.flows.push('Duplicate cash-out and changed-payload conflict leave balance unchanged')

  await page.getByRole('button', { name: /Mute sound/ }).click()
  const soundsBefore = await page.evaluate(() => window.__hiloAudio.length)
  await page.getByLabel('Wager amount').fill('1.00')
  await start().focus(); await page.keyboard.press('Enter')
  await expect(collect()).toBeEnabled()
  assert.equal(await page.evaluate(() => window.__hiloAudio.length), soundsBefore)
  await page.getByRole('button', { name: /Turn sound on/ }).click()
  await call(collect())
  const playedSounds = await page.evaluate(() => window.__hiloAudio)
  assert.ok(playedSounds.length > soundsBefore)
  assert.ok(playedSounds.some(src => /chips-collide/.test(src)))
  report.flows.push('Keyboard starts round, mute suppresses media, collect plays approved sound')
  await page.screenshot({ path: new URL('phone-390-collected.png', output).pathname.replace(/^\/(\w:)/,'$1'), fullPage: true })

  // Force a real loss by taking low-probability valid calls; outcome remains entirely server-generated.
  next = await call(start())
  for (let attempt = 0; attempt < 20; attempt++) {
    if (next.round.status !== 'active') next = await call(start())
    const choice = next.round.higher.available && (!next.round.lower.available || next.round.higher.probability < next.round.lower.probability) ? 'higher' : 'lower'
    next = await call(choice === 'higher' ? higher() : lower())
    if (next.round.status === 'lost') break
  }
  assert.equal(next.round.status, 'lost')
  assert.equal(next.round.currentReturnCents, 0)
  await expect(start()).toBeEnabled()
  await page.screenshot({ path: new URL('phone-390-lost.png', output).pathname.replace(/^\/(\w:)/,'$1'), fullPage: true })
  report.flows.push('Real server loss resets return and permits a new wager')
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await call(start())
  assert.equal(await page.locator('.hilo-card-reveal').evaluate(el => getComputedStyle(el).animationName), 'none')
  await call(collect())
  report.flows.push('Reduced motion disables card animation; full round remains playable')
  const soundContext = await browser.newContext({ viewport: { width: 1024, height: 768 } })
  try {
    const soundPage = await soundContext.newPage()
    await soundPage.addInitScript(() => {
      window.__realHiLoAudio = []
      const play = HTMLMediaElement.prototype.play
      HTMLMediaElement.prototype.play = function () {
        const source = this.src
        const promise = play.call(this)
        promise?.then(() => window.__realHiLoAudio.push({ source, played: true }), error => window.__realHiLoAudio.push({ source, played: false, error: error.message }))
        return promise
      }
    })
    await soundPage.goto(origin)
    await soundPage.getByRole('button', { name: 'Deal cards' }).click()
    await expect(soundPage.getByRole('button', { name: /^Cash out/ })).toBeEnabled()
    await soundPage.getByRole('button', { name: /^Cash out/ }).click()
    await expect(soundPage.getByRole('button', { name: 'Deal cards' })).toBeEnabled()
    await expect.poll(() => soundPage.evaluate(() => window.__realHiLoAudio.filter(item => item.played).length)).toBeGreaterThanOrEqual(3)
    report.realAudio = await soundPage.evaluate(() => window.__realHiLoAudio)
    assert.equal(report.realAudio.every(item => item.played), true)
    report.flows.push('Native media playback successfully decodes shuffle, deal and collect assets')
  } finally { await soundContext.close() }
  assert.deepEqual(errors, [])
  report.browserErrors = errors
  report.audioSources = [...new Set(playedSounds)]
  await writeFile(new URL('browser-verification.json', output), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report, null, 2))
} finally { await context.close(); await browser.close() }

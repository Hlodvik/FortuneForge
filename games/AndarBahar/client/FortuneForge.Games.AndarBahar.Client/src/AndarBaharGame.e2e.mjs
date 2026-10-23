// Start the isolated sample API and Vite server, then run npm run test:e2e.
// ANDAR_BAHAR_URL, ANDAR_BAHAR_BROWSER_CHANNEL and ANDAR_BAHAR_SCREENSHOTS are optional.
import assert from 'node:assert/strict'
import { mkdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '@playwright/test'

const origin = process.env.ANDAR_BAHAR_URL || 'http://127.0.0.1:5182'
const browser = await chromium.launch({ headless: true, ...(process.env.ANDAR_BAHAR_BROWSER_CHANNEL ? { channel: process.env.ANDAR_BAHAR_BROWSER_CHANNEL } : {}) })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
await context.addInitScript(() => {
  window.__andarBaharAudio = []
  window.__andarBaharMedia = []
  const original = HTMLMediaElement.prototype.play
  HTMLMediaElement.prototype.play = function () {
    window.__andarBaharAudio.push(this.src)
    const playback = { src: this.src, loaded: false, fulfilled: false, ended: false, duration: null, error: null, stopped: false }
    window.__andarBaharMedia.push(playback)
    this.addEventListener('loadeddata', () => { playback.loaded = this.readyState >= 2; playback.duration = this.duration }, { once: true })
    this.addEventListener('ended', () => { playback.ended = true; playback.duration = this.duration }, { once: true })
    this.addEventListener('error', () => { playback.error = `MediaError ${this.error?.code}: ${this.error?.message}` }, { once: true })
    const promise = original.call(this)
    promise?.then(() => { playback.fulfilled = true }, error => {
      if (error.name === 'AbortError') playback.stopped = true
      else playback.error = `${error.name}: ${error.message}`
    })
    return promise
  }
})
const page = await context.newPage()
page.setDefaultTimeout(10000)
const errors = []
let audioEvidence = []
page.on('pageerror', error => errors.push(error.message))
const screenshots = process.env.ANDAR_BAHAR_SCREENSHOTS ? fileURLToPath(new URL('../.e2e-artifacts/', import.meta.url)) : null
if (screenshots) await mkdir(screenshots, { recursive: true })
const money = value => `R${Math.abs(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const api = path => `${origin}/api/games/andar-bahar${path}`
const pendingKey = 'fortuneforge:andar-bahar:pending:standalone-practice'
const andar = () => page.getByRole('button', { name: /^Bet on Andar,/ })
const bahar = () => page.getByRole('button', { name: /^Bet on Bahar,/ })
const chip = value => page.getByRole('button', { name: `${money(value)} chip`, exact: true })
const deal = () => page.getByRole('button', { name: /Deal cards/ })
const clear = () => page.getByRole('button', { name: /Clear/ })
const total = () => page.getByTestId('andar-bahar-total-bet')
const balance = () => page.getByTestId('andar-bahar-balance')
const closeResult = async () => {
  const button = page.getByRole('button', { name: /Continue|Next round|Close result/ }).first()
  if (await button.isVisible()) await button.click()
}
const skip = async () => {
  const button = page.getByRole('button', { name: /Skip deal/ })
  if (await button.isVisible()) await button.click()
}
const readStatus = async (request = context.request) => {
  const response = await request.get(api('/status'))
  assert.equal(response.status(), 200)
  const status = await response.json()
  assert.equal(status.mode, 'development-practice-andar-first', 'E2E must use the isolated development practice wallet')
  return status
}
const assertSettlement = (round, before) => {
  assert.deepEqual(Object.keys(round).sort(), ['roundId', 'balance', 'phase', 'joker', 'deals', 'winner', 'side', 'stake', 'disposition', 'profit', 'totalReturn'].sort())
  assert.equal(round.phase, 'settled')
  assert.ok(round.deals.length >= 1 && round.deals.length <= 49)
  const seen = new Set([`${round.joker.rank}:${round.joker.suit}`])
  round.deals.forEach((dealt, index) => {
    assert.deepEqual(Object.keys(dealt).sort(), ['number', 'side', 'card'].sort())
    assert.equal(dealt.number, index + 1)
    assert.equal(dealt.side, index % 2 === 0 ? 'andar' : 'bahar')
    const card = `${dealt.card.rank}:${dealt.card.suit}`
    assert.equal(seen.has(card), false, 'Single deck must not repeat a card')
    seen.add(card)
    assert.equal(dealt.card.rank === round.joker.rank, index === round.deals.length - 1, 'Only the final played card may match')
  })
  assert.equal(round.winner, round.deals.at(-1).side)
  const won = round.side === round.winner
  const expectedReturn = won ? Math.round(round.stake * (round.side === 'andar' ? 190 : 200)) / 100 : 0
  assert.equal(round.disposition, won ? 'win' : 'loss')
  assert.equal(round.totalReturn, expectedReturn)
  assert.equal(Math.round(round.profit * 100), Math.round((expectedReturn - round.stake) * 100))
  assert.equal(Math.round(round.balance * 100), Math.round((before - round.stake + expectedReturn) * 100))
}
const assertNoOverflow = async name => {
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${name}: horizontal overflow`)
}
const snapshot = async name => {
  if (screenshots) await page.screenshot({ path: `${screenshots}${name}.png`, fullPage: true })
}
const assertResultLayout = async viewport => {
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  const bounds = await dialog.boundingBox()
  assert.ok(bounds && bounds.x >= -1 && bounds.y >= -1 && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1, `${viewport.name}: result dialog clips outside viewport`)
  const overflow = await dialog.evaluate(element => ({ hidden: getComputedStyle(element).overflowY === 'hidden', content: element.scrollHeight, visible: element.clientHeight }))
  assert.equal(overflow.hidden && overflow.content > overflow.visible + 1, false, `${viewport.name}: result dialog hides content without scrolling`)
  for (const card of await dialog.locator('.ff-ab__card').all()) {
    const box = await card.boundingBox()
    assert.ok(box && Math.abs(box.height / box.width - 1.4) < .03, `${viewport.name}: result card proportions are distorted`)
  }
  const action = dialog.getByRole('button', { name: /Continue/ })
  await action.scrollIntoViewIfNeeded()
  await expect(action).toBeInViewport({ ratio: 1 })
}

try {
  await page.goto(origin)
  await expect(andar()).toBeEnabled()
  await expect(page.getByText(/Development practice/i).first()).toBeVisible()
  const before = (await readStatus()).balance

  // Pointer, keyboard activation, repeated chip piles, undo and side switching.
  await chip(2).focus(); await page.keyboard.press('Enter')
  await andar().focus(); await page.keyboard.press('Space')
  await andar().click(); await andar().click()
  await expect(total()).toHaveText('R6.00')
  await expect(andar()).toHaveAccessibleName(/current bet R6.00/)
  await expect(andar().getByLabel('3 chips placed').locator('img')).toHaveCount(3)
  await snapshot('desktop-chips')
  await page.getByRole('button', { name: /Undo/ }).click()
  await expect(total()).toHaveText('R4.00')
  await bahar().click()
  await expect(total()).toHaveText('R2.00')
  await clear().click()
  await expect(total()).toHaveText('R0.00')

  // Audio calls are observed through the actual cue path; muting stops further cues.
  assert.ok((await page.evaluate(() => window.__andarBaharAudio)).some(src => src.includes('.ogg')), 'A chip action should play the shared sound cue')
  await expect.poll(() => page.evaluate(() => window.__andarBaharMedia.some(playback => playback.src.includes('chip-lay-1.ogg') && playback.ended && !playback.error))).toBe(true)
  await page.getByRole('button', { name: /Mute sound/ }).click()
  const mutedCount = await page.evaluate(() => window.__andarBaharAudio.length)
  await chip(.5).click(); await andar().click()
  assert.equal(await page.evaluate(() => window.__andarBaharAudio.length), mutedCount)
  await page.getByRole('button', { name: /Turn sound on/ }).click()

  const firstResponse = page.waitForResponse(response => response.url().endsWith('/api/games/andar-bahar/rounds') && response.request().method() === 'POST')
  await deal().focus(); await page.keyboard.press('Enter')
  const response = await firstResponse
  assert.equal(response.status(), 201)
  const submitted = response.request()
  const originalKey = submitted.headers()['idempotency-key']
  assert.match(originalKey, /^andar-bahar-[A-Za-z0-9_-]{16,100}$/)
  assert.deepEqual(submitted.postDataJSON(), { side: 'andar', stake: .5 })
  const round = await response.json()
  assertSettlement(round, before)
  await expect(page.getByRole('button', { name: /Skip deal/ })).toBeVisible()
  await expect(page.getByLabel('Andar cards', { exact: true }).locator('.ff-ab__card')).toHaveCount(0)
  await expect(page.getByLabel('Andar cards', { exact: true }).locator('.ff-ab__card')).toHaveCount(1)
  await expect(page.getByLabel('Bahar cards', { exact: true }).locator('.ff-ab__card')).toHaveCount(0)
  await snapshot('desktop-dealing')
  await skip()
  await expect(balance()).toHaveText(money(round.balance), { timeout: 20000 })
  await assertResultLayout({ name: 'desktop', width: 1440, height: 1000 })
  await snapshot('desktop-result')
  for (const viewport of [{ name: 'portrait-320', width: 320, height: 844 }, { name: 'portrait-430', width: 430, height: 844 }, { name: 'short-landscape', width: 844, height: 390 }, { name: 'small-landscape', width: 568, height: 320 }]) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await assertResultLayout(viewport)
    await assertNoOverflow(`${viewport.name} result`)
    await snapshot(`${viewport.name}-result`)
  }
  await page.setViewportSize({ width: 1440, height: 1000 })
  await closeResult()
  await expect(andar()).toBeEnabled()
  assert.equal(await page.evaluate(key => sessionStorage.getItem(key), pendingKey), null)
  // Check the browser's real media pipeline for all three cues, then independently decode
  // the served OGG bytes and prove they contain a non-silent waveform.
  const cueNames = ['chip-lay-1.ogg', 'card-place-1.ogg', 'chips-collide-1.ogg']
  try {
    await expect.poll(() => page.evaluate(names => names.every(name => window.__andarBaharMedia.some(playback => playback.src.includes(name) && playback.loaded && playback.fulfilled && playback.ended && playback.duration > 0 && !playback.error)), cueNames)).toBe(true)
  } catch (error) {
    console.error('Audio pipeline diagnostics:', JSON.stringify(await page.evaluate(() => window.__andarBaharMedia)))
    throw error
  }
  const mediaErrors = await page.evaluate(() => window.__andarBaharMedia.filter(playback => playback.error).map(playback => playback.error))
  assert.deepEqual(mediaErrors, [], 'Shared sound cues must load and play without browser media errors')
  audioEvidence = await page.evaluate(async names => {
    const audioContext = new AudioContext()
    try {
      return await Promise.all(names.map(async name => {
        const source = window.__andarBaharMedia.find(playback => playback.src.includes(name)).src
        const response = await fetch(source)
        if (!response.ok) throw new Error(`${name}: HTTP ${response.status}`)
        const buffer = await audioContext.decodeAudioData(await response.arrayBuffer())
        let peak = 0
        for (let channel = 0; channel < buffer.numberOfChannels; channel++) {
          const samples = buffer.getChannelData(channel)
          for (let index = 0; index < samples.length; index++) peak = Math.max(peak, Math.abs(samples[index]))
        }
        return { cue: name, duration: buffer.duration, channels: buffer.numberOfChannels, sampleRate: buffer.sampleRate, frames: buffer.length, peak }
      }))
    } finally { await audioContext.close() }
  }, cueNames)
  for (const cue of audioEvidence) assert.ok(cue.duration > 0 && cue.channels >= 1 && cue.sampleRate > 0 && cue.frames > 0 && cue.peak > 0, `${cue.cue}: OGG decoding must produce audible samples`)
  await page.getByRole('button', { name: /Mute sound/ }).click()

  const replay = await context.request.post(api('/rounds'), { headers: { 'Idempotency-Key': originalKey }, data: submitted.postDataJSON() })
  assert.equal(replay.status(), 201)
  const replayed = await replay.json()
  assert.equal(replayed.roundId, round.roundId)
  assert.deepEqual(replayed.deals, round.deals)
  assert.equal((await readStatus()).balance, round.balance)
  const changed = await context.request.post(api('/rounds'), { headers: { 'Idempotency-Key': originalKey }, data: { side: 'bahar', stake: .5 } })
  assert.equal(changed.status(), 409)
  assert.equal((await readStatus()).balance, round.balance)

  // The preview assigns a separate cookie wallet; other contexts cannot fetch this round.
  const stranger = await browser.newContext()
  const strangerStatus = await readStatus(stranger.request)
  assert.equal(strangerStatus.balance, 10000)
  assert.equal((await stranger.request.get(api('/rounds/latest'))).status(), 204)
  assert.equal((await stranger.request.get(api(`/rounds/${round.roundId}`))).status(), 404)
  await stranger.close()

  // Lose the HTTP response only after the actual sample server settled the wager.
  await clear().click(); await chip(1).click(); await bahar().click()
  const beforeRecovery = (await readStatus()).balance
  let lostRound, lostKey, lostBody
  await page.route('**/api/games/andar-bahar/rounds', async route => {
    lostKey = route.request().headers()['idempotency-key']
    lostBody = route.request().postDataJSON()
    const serverResponse = await route.fetch()
    assert.equal(serverResponse.status(), 201)
    lostRound = await serverResponse.json()
    await route.abort('failed')
  })
  await deal().click()
  await expect(page.getByRole('button', { name: /Restore deal/ })).toBeEnabled()
  await expect(andar()).toBeDisabled()
  assertSettlement(lostRound, beforeRecovery)
  const pending = await page.evaluate(key => JSON.parse(sessionStorage.getItem(key)), pendingKey)
  assert.equal(pending.key, lostKey)
  assert.equal(pending.stake, lostBody.stake)
  await snapshot('connection-recovery')
  await page.unroute('**/api/games/andar-bahar/rounds')
  const restoreResponse = page.waitForResponse(value => value.url().endsWith('/api/games/andar-bahar/rounds') && value.request().method() === 'POST')
  await page.reload()
  const restored = await restoreResponse
  assert.equal(restored.request().headers()['idempotency-key'], lostKey)
  assert.deepEqual(restored.request().postDataJSON(), lostBody)
  assert.deepEqual((await restored.json()).deals, lostRound.deals)
  await skip()
  await expect(balance()).toHaveText(money(lostRound.balance), { timeout: 20000 })
  await closeResult()
  assert.equal((await readStatus()).balance, lostRound.balance)
  assert.equal(await page.evaluate(key => sessionStorage.getItem(key), pendingKey), null)

  // Refreshing the result restores the server's prefix and wallet with no POST.
  let refreshPosts = 0
  const countPosts = request => { if (request.method() === 'POST' && request.url().endsWith('/rounds')) refreshPosts++ }
  page.on('request', countPosts)
  await page.reload()
  await expect(andar()).toBeEnabled()
  await expect(balance()).toHaveText(money(lostRound.balance))
  assert.equal(refreshPosts, 0)
  page.off('request', countPosts)

  // Insufficient funds is a confirmed rejection: release the pending key and allow editing.
  await clear().click(); await chip(.5).click(); await andar().click()
  await page.route('**/api/games/andar-bahar/rounds', route => route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ code: 'insufficient-slot-credits', message: 'Insufficient practice balance.' }) }))
  await deal().click()
  await expect(page.getByText('Insufficient practice balance.', { exact: true })).toBeVisible()
  await expect(bahar()).toBeEnabled()
  assert.equal(await page.evaluate(key => sessionStorage.getItem(key), pendingKey), null)
  await page.unroute('**/api/games/andar-bahar/rounds')
  assert.equal((await readStatus()).balance, lostRound.balance)
  await clear().click()

  const viewports = [
    { name: 'desktop', width: 1440, height: 1000 },
    ...[320, 360, 375, 390, 430].map(width => ({ name: `portrait-${width}`, width, height: 844 })),
    { name: 'short-landscape', width: 844, height: 390 },
    { name: 'small-landscape', width: 568, height: 320 },
  ]
  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await assertNoOverflow(viewport.name)
    const announcement = await page.getByRole('status').boundingBox()
    const betting = await page.getByRole('group', { name: 'Choose your betting side' }).boundingBox()
    assert.ok(announcement && betting && announcement.y + announcement.height <= betting.y + 1, `${viewport.name}: status announcement overlaps betting controls`)
    for (const spot of [andar(), bahar()]) {
      const box = await spot.boundingBox()
      assert.ok(box && box.x >= -1 && box.x + box.width <= viewport.width + 1 && box.height >= 44, `${viewport.name}: betting spot clips or is too small`)
    }
    await chip(.5).click(); await andar().click()
    await expect(total()).toHaveText('R0.50')
    await expect(deal()).toBeEnabled()
    await clear().click()
    await snapshot(viewport.name)
  }

  await page.getByRole('button', { name: /How to play/ }).click()
  const rules = page.getByRole('dialog')
  await expect(rules).toBeVisible()
  await expect(rules).toContainText(/Andar.*first/i)
  await expect(rules).toContainText(/0\.9|9:10|90%/)
  await page.getByRole('button', { name: 'Close rules' }).click()
  await expect(rules).toHaveCount(0)

  // A separate real touch context proves tap controls and reduced-motion settlement.
  const touchContext = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, reducedMotion: 'reduce' })
  const touch = await touchContext.newPage()
  touch.on('pageerror', error => errors.push(error.message))
  await touch.goto(origin)
  const touchAndar = touch.getByRole('button', { name: /^Bet on Andar,/ })
  await expect(touchAndar).toBeEnabled()
  await touch.getByRole('button', { name: 'R0.50 chip', exact: true }).tap()
  await touchAndar.tap()
  await expect(touch.getByTestId('andar-bahar-total-bet')).toHaveText('R0.50')
  const touchResponse = touch.waitForResponse(value => value.url().endsWith('/api/games/andar-bahar/rounds') && value.request().method() === 'POST')
  await touch.getByRole('button', { name: /Deal cards/ }).tap()
  const touchRound = await (await touchResponse).json()
  assertSettlement(touchRound, 10000)
  await expect(touch.getByTestId('andar-bahar-balance')).toHaveText(money(touchRound.balance))
  if (screenshots) await touch.screenshot({ path: `${screenshots}touch-result.png`, fullPage: true })
  await touchContext.close()

  // Two tabs sharing one wallet must retain independent unresolved requests. Finishing
  // one tab cannot erase the other tab's key; its reload must replay its own wager.
  const tabsContext = await browser.newContext({ reducedMotion: 'reduce' })
  const tabs = await Promise.all([tabsContext.newPage(), tabsContext.newPage()])
  const tabRounds = [], tabKeys = []
  await Promise.all(tabs.map(async (tab, index) => {
    tab.on('pageerror', error => errors.push(error.message))
    await tab.goto(origin)
    await expect(tab.getByRole('button', { name: /^Bet on Andar,/ })).toBeEnabled()
    assert.equal(await tab.evaluate(key => sessionStorage.getItem(key), pendingKey), null)
    await tab.getByRole('button', { name: 'R0.50 chip', exact: true }).click()
    await tab.getByRole('button', { name: index === 0 ? /^Bet on Andar,/ : /^Bet on Bahar,/ }).click()
    await tab.route('**/api/games/andar-bahar/rounds', async route => {
      tabKeys[index] = route.request().headers()['idempotency-key']
      const settledResponse = await route.fetch()
      assert.equal(settledResponse.status(), 201)
      tabRounds[index] = await settledResponse.json()
      await route.abort('failed')
    })
  }))
  // Both pages now share the same established cookie before either POST begins.
  const tabsBefore = (await readStatus(tabsContext.request)).balance
  await Promise.all(tabs.map(tab => tab.getByRole('button', { name: /Deal cards/ }).click()))
  await Promise.all(tabs.map(tab => expect(tab.getByRole('button', { name: /Restore deal/ })).toBeEnabled()))
  assert.notEqual(tabKeys[0], tabKeys[1])
  for (let index = 0; index < tabs.length; index++) {
    assert.equal((await tabs[index].evaluate(key => JSON.parse(sessionStorage.getItem(key)), pendingKey)).key, tabKeys[index])
    assertSettlement(tabRounds[index], tabRounds[index].balance - tabRounds[index].profit)
    await tabs[index].unroute('**/api/games/andar-bahar/rounds')
  }
  const tabsBalance = Math.round((tabsBefore + tabRounds[0].profit + tabRounds[1].profit) * 100) / 100
  assert.equal((await readStatus(tabsContext.request)).balance, tabsBalance)
  await tabs[0].getByRole('button', { name: /Restore deal/ }).click()
  await expect.poll(() => tabs[0].evaluate(key => sessionStorage.getItem(key), pendingKey)).toBeNull()
  assert.equal((await tabs[1].evaluate(key => JSON.parse(sessionStorage.getItem(key)), pendingKey)).key, tabKeys[1], 'Finishing tab A must retain tab B\'s pending key')
  const secondTabResponse = tabs[1].waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/api/games/andar-bahar/rounds'))
  await tabs[1].reload()
  const secondTabReplay = await secondTabResponse
  assert.equal(secondTabReplay.request().headers()['idempotency-key'], tabKeys[1])
  assert.equal((await secondTabReplay.json()).roundId, tabRounds[1].roundId)
  await expect.poll(() => tabs[1].evaluate(key => sessionStorage.getItem(key), pendingKey)).toBeNull()
  assert.equal((await readStatus(tabsContext.request)).balance, tabsBalance)
  await tabsContext.close()

  // Initial connectivity failure offers reconnect and never enables a wager without status.
  await page.route('**/api/games/andar-bahar/status', route => route.abort('failed'))
  await page.reload()
  await expect(page.getByRole('button', { name: /Reconnect/ })).toBeVisible()
  await expect(andar()).toBeDisabled()
  await page.unroute('**/api/games/andar-bahar/status')
  await page.getByRole('button', { name: /Reconnect/ }).click()
  await expect(andar()).toBeEnabled()
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ result: 'passed', origin, firstWinner: round.winner, recoveryWinner: lostRound.winner, checks: ['real settlement and played-prefix math', 'individual paced dealing and skip', 'idempotency replay and changed-body conflict', 'cookie wallet privacy', 'response-loss and reload recovery', 'two tabs retain independent pending keys and settle without duplicate debit', 'result refresh without new wager', 'confirmed insufficient-funds UI recovery', 'keyboard and touch', 'all OGG cues loaded, played and decoded to non-silent PCM; mute prevents dispatch', '320–430px portrait and short landscape', 'rules and reconnect', 'no browser errors'], audioEvidence, ...(screenshots ? { screenshots } : {}) }, null, 2))
} catch (error) {
  if (screenshots) await page.screenshot({ path: `${screenshots}failure.png`, fullPage: true }).catch(() => undefined)
  throw error
} finally {
  await browser.close()
}

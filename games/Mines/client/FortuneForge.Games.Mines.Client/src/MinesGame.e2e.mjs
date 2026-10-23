// Start the isolated practice API (5201) and Vite client (5181), then run with Node.
// Optional: MINES_URL, MINES_BROWSER_CHANNEL. All wagers use development play credits.
import assert from 'node:assert/strict'
import { mkdir, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '@playwright/test'

const origin = process.env.MINES_URL || 'http://127.0.0.1:5181'
const evidence = fileURLToPath(new URL('../../../evidence/', import.meta.url))
await mkdir(evidence, { recursive: true })
const pendingKey = 'fortuneforge:mines:pending:standalone-practice'
const money = cents => `R${(cents / 100).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const choose = (n, k) => {
  let value = 1n
  for (let i = 1; i <= Math.min(k, n - k); i++) value = value * BigInt(n - Math.min(k, n - k) + i) / BigInt(i)
  return value
}
const exactReturn = round => Number(BigInt(round.wagerCents) * 99n * choose(25, round.safeReveals) / (100n * choose(25 - round.mineCount, round.safeReveals)))
let browser
try {
  browser = await chromium.launch({ headless: true, ...(process.env.MINES_BROWSER_CHANNEL ? { channel: process.env.MINES_BROWSER_CHANNEL } : {}) })
} catch (error) {
  if (process.env.MINES_BROWSER_CHANNEL) throw error
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
}
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } })
await context.addInitScript(() => {
  window.__minesAudio = { plays: 0, oscillators: 0 }
  const nativePlay = HTMLMediaElement.prototype.play
  HTMLMediaElement.prototype.play = function (...args) { window.__minesAudio.plays++; return nativePlay.apply(this, args) }
  if (window.AudioContext) {
    const nativeOscillator = AudioContext.prototype.createOscillator
    AudioContext.prototype.createOscillator = function (...args) { window.__minesAudio.oscillators++; return nativeOscillator.apply(this, args) }
  }
})
const page = await context.newPage()
page.setDefaultTimeout(10_000)
const errors = [], checks = [], viewports = []
page.on('pageerror', error => errors.push(error.message))
const api = suffix => `${origin}/api/games/mines${suffix}`
const readSession = async () => {
  const response = await context.request.get(api('/session'))
  assert.equal(response.status(), 200, 'Practice session is available')
  const session = await response.json()
  assert.equal(session.practice, true, 'This test must never wager production funds')
  return session
}
const primary = () => page.locator('.ff-mines__primary-actions .ff-mines__primary')
const wager = () => page.getByRole('textbox', { name: 'Wager amount' })
const count = () => page.getByRole('combobox', { name: 'Mine count' })
const tile = index => page.locator(`[data-tile="${index}"]`)
const screenshot = async name => { await page.screenshot({ path: `${evidence}${name}.png`, animations: 'disabled' }) }
const confirmed = async action => {
  const mutation = page.waitForResponse(response => response.url().includes('/api/games/mines/rounds') && response.request().method() === 'POST')
  const refresh = page.waitForResponse(response => response.url().endsWith('/api/games/mines/session') && response.request().method() === 'GET')
  await action()
  const result = await mutation
  assert.ok([200, 201].includes(result.status()), `Successful mutation, got ${result.status()}`)
  const value = await result.json()
  assert.equal((await refresh).status(), 200)
  await expect(page.locator('.ff-mines__status')).not.toContainText('Confirming')
  await expect(page.getByTestId('mines-balance')).toHaveText(money(value.balanceCents))
  return { value, request: result.request() }
}
const begin = async (mines = 1, amount = '1.00') => {
  if (await page.getByRole('button', { name: 'Clear board' }).isVisible()) await page.getByRole('button', { name: 'Clear board' }).click()
  await expect(wager()).toBeEnabled()
  await wager().fill(amount)
  await count().selectOption(String(mines))
  const before = await readSession()
  const result = await confirmed(() => primary().click())
  assert.equal(result.value.round.status, 'active')
  assert.equal(result.value.round.mineCount, mines)
  assert.equal(result.value.balanceCents, before.balanceCents - Math.round(Number(amount) * 100))
  assert.equal(result.value.round.tiles.filter(tile => tile.kind === 'hidden').length, 25)
  assert.deepEqual(Object.keys(result.value.round).sort(), ['roundId', 'wagerCents', 'mineCount', 'status', 'safeReveals', 'gemsRemaining', 'currentMultiplier', 'nextMultiplier', 'cashOutCents', 'canCashOut', 'payoutCents', 'tiles'].sort())
  return result.value
}
const firstGem = async () => {
  let state = await readSession()
  for (let attempt = 0; attempt < 20; attempt++) {
    if (state.round?.status !== 'active') state = await begin(1)
    if (state.round.safeReveals > 0) return state
    const covered = state.round.tiles.find(tile => !tile.isRevealed).index
    state = (await confirmed(() => tile(covered).click())).value
  }
  throw new Error('Could not find a safe practice tile after 20 independent rounds')
}
const cashOut = async () => {
  const before = await firstGem()
  const { value } = await confirmed(() => page.getByRole('button', { name: /^Cash out/ }).click())
  assert.equal(value.round.status, 'cashed-out')
  assert.equal(value.round.payoutCents, exactReturn(before.round))
  assert.equal(value.balanceCents, before.balanceCents + value.round.payoutCents)
  assert.equal(value.round.tiles.filter(tile => tile.kind === 'hidden').length, 0)
  return value
}
const pending = () => page.evaluate(key => {
  const raw = localStorage.getItem(key)
  return raw ? JSON.parse(raw) : null
}, pendingKey)

try {
  await page.goto(origin)
  await expect(primary()).toBeEnabled()
  assert.equal((await readSession()).practice, true)
  await expect(page.getByText('PRACTICE BALANCE', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Mute sound', exact: true }).click()
  const mutedAudio = await page.evaluate(() => ({ ...window.__minesAudio }))
  await begin(1)
  await expect(wager()).toBeDisabled()
  await expect(count()).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Cash out', exact: true })).toBeDisabled()
  let active = await firstGem()
  assert.equal(active.round.cashOutCents, exactReturn(active.round))
  assert.deepEqual(await page.evaluate(() => window.__minesAudio), mutedAudio, 'Mute prevents wager and reveal sounds')
  await screenshot('desktop-active')
  checks.push('Practice label, predetermined private board, debit, locked wager/mines, no zero-pick cash-out, exact displayed cash-out')

  await page.reload()
  await expect(page.getByRole('button', { name: /^Cash out/ })).toBeEnabled()
  assert.deepEqual((await readSession()).round, active.round, 'Refresh resumes the same private board and reveal sequence')
  await expect(page.getByRole('button', { name: 'Turn sound on', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Turn sound on', exact: true }).click()
  let keyboardReveal
  for (let attempt = 0; attempt < 20; attempt++) {
    const roving = page.locator('[data-tile][tabindex="0"]')
    await expect(roving).toHaveCount(1)
    await roving.focus()
    const priorFocused = await roving.getAttribute('data-tile')
    await page.keyboard.press('ArrowRight')
    assert.notEqual(await page.locator('[data-tile]:focus').getAttribute('data-tile'), priorFocused)
    keyboardReveal = await confirmed(() => page.keyboard.press('Space'))
    if (keyboardReveal.value.round.status === 'active') break
    await firstGem()
  }
  assert.equal(keyboardReveal.value.round.status, 'active', 'Verify keyboard focus after a safe reveal on a genuinely random board')
  assert.equal(keyboardReveal.value.round.safeReveals, 2)
  await expect(page.locator('[data-tile][tabindex="0"]:not(:disabled)')).toHaveCount(1)
  await expect(page.getByRole('button', { name: /^Cash out/ })).toBeEnabled()
  await expect(page.locator('.ff-mines__payout-rail > div').first().locator('strong'))
    .toHaveText(await page.getByTestId('mines-next').textContent())
  await screenshot('desktop-active-multiple')
  const settled = await cashOut()
  await expect(page.locator('.ff-mines__result')).toContainText('CASHED OUT')
  await expect(page.locator('.ff-mines__result')).toContainText(money(settled.round.payoutCents))
  await screenshot('desktop-cashout')
  checks.push('Refresh resumes active round; mute persists; arrow plus Space reveals; keyboard board remains reachable; cash-out credits exact payout and reveals terminal board')

  // A real server chooses every board. High mine count makes the loss path efficient without any client-known layout.
  let lost
  for (let attempt = 0; attempt < 8; attempt++) {
    await begin(24, '0.10')
    lost = (await confirmed(() => tile(0).click())).value
    if (lost.round.status === 'lost') break
    assert.equal(lost.round.status, 'cleared')
    assert.equal(lost.round.payoutCents, exactReturn(lost.round))
  }
  assert.equal(lost.round.status, 'lost')
  assert.equal(lost.round.payoutCents, 0)
  assert.equal(lost.round.tiles.filter(tile => tile.kind === 'mine').length, 24)
  assert.equal(lost.round.tiles.filter(tile => tile.kind === 'hidden').length, 0)
  await expect(page.locator('.ff-mines__result')).toContainText('MINE FOUND')
  await screenshot('desktop-loss')
  const playedAudio = await page.evaluate(() => window.__minesAudio)
  assert.ok(playedAudio.plays > 0 && playedAudio.oscillators > 0, 'Unmuted wager and result use actual browser audio APIs')
  checks.push('Immediate mine loss reveals board, no payout; unmuted wager and gem/mine/cash-out audio APIs exercised')

  await page.getByRole('button', { name: 'Clear board' }).click()
  await wager().fill('1.00'); await count().selectOption('3')
  const beforeInterrupted = await readSession()
  let interrupted, submitted
  const startPattern = '**/api/games/mines/rounds'
  await page.route(startPattern, async route => {
    submitted = route.request().postDataJSON()
    const response = await route.fetch()
    assert.equal(response.status(), 201)
    interrupted = await response.json()
    await route.abort('failed')
  })
  await primary().click()
  await expect(page.getByRole('button', { name: /^Restore round/ })).toBeEnabled()
  assert.equal(interrupted.balanceCents, beforeInterrupted.balanceCents - 100)
  assert.equal((await readSession()).balanceCents, interrupted.balanceCents)
  assert.deepEqual(await pending(), { action: 'start', ...submitted })
  await expect(wager()).toBeDisabled(); await expect(count()).toBeDisabled()
  await page.unroute(startPattern)
  const replay = await confirmed(() => page.getByRole('button', { name: /^Restore round/ }).click())
  assert.deepEqual(replay.request.postDataJSON(), submitted, 'Recovery preserves original requestId and exact command payload')
  assert.deepEqual(replay.value, interrupted, 'The original command receipt replays exactly')
  assert.equal((await readSession()).balanceCents, interrupted.balanceCents, 'No duplicate debit after replay')
  assert.equal(await pending(), null)
  checks.push('Lost start response after durable debit; Restore retries identical requestId/payload; exact receipt replay; no second debit')

  // A second lost response is recovered from persisted pending storage by a full reload.
  let revealReceipt, revealPayload
  const roundId = (await readSession()).round.roundId
  const revealPattern = `**/api/games/mines/rounds/${roundId}/reveal`
  await page.route(revealPattern, async route => {
    revealPayload = route.request().postDataJSON()
    const response = await route.fetch()
    assert.equal(response.status(), 200)
    revealReceipt = await response.json()
    await route.abort('failed')
  })
  await tile(0).click()
  await expect(page.getByRole('button', { name: /^Restore round/ })).toBeEnabled()
  assert.deepEqual(await pending(), { action: 'reveal', roundId, ...revealPayload })
  await page.unroute(revealPattern)
  const reloadReplay = page.waitForResponse(response => response.url().endsWith(`/rounds/${roundId}/reveal`) && response.request().method() === 'POST')
  await page.reload()
  const reloadResponse = await reloadReplay
  assert.deepEqual(reloadResponse.request().postDataJSON(), revealPayload)
  assert.deepEqual(await reloadResponse.json(), revealReceipt)
  await expect(page.locator('.ff-mines__status')).not.toContainText('Confirming')
  await expect(page.getByTestId('mines-balance')).toHaveText(money(revealReceipt.balanceCents))
  await expect.poll(pending).toBeNull()
  assert.deepEqual((await readSession()).round, revealReceipt.round)
  checks.push('Full refresh replays persisted reveal identity exactly and recovers authoritative current state without a second reveal')

  active = await firstGem()
  await page.setViewportSize({ width: 1440, height: 900 })
  await screenshot('desktop-active')
  const dimensions = [
    { name: 'desktop', width: 1440, height: 900 },
    { name: 'portrait320', width: 320, height: 568 },
    { name: 'portrait360', width: 360, height: 640 },
    { name: 'portrait390', width: 390, height: 844 },
    { name: 'portrait430', width: 430, height: 932 },
    { name: 'landscape844', width: 844, height: 390 },
    { name: 'landscape667', width: 667, height: 375 },
  ]
  for (const viewport of dimensions) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await page.locator('.ff-mines__board').scrollIntoViewIfNeeded()
    const measure = await page.evaluate(() => {
      const root = document.documentElement, body = document.querySelector('.in-game-shell__body')
      const box = element => { const rect = element.getBoundingClientRect(); return { x: rect.x, y: rect.y, width: rect.width, height: rect.height, right: rect.right, bottom: rect.bottom } }
      return { documentWidth: root.scrollWidth, documentHeight: root.scrollHeight, innerWidth, innerHeight,
        bodyScroll: body ? { client: body.clientHeight, scroll: body.scrollHeight } : null,
        board: box(document.querySelector('.ff-mines__board')), firstTile: box(document.querySelector('[data-tile]')),
        table: box(document.querySelector('.ff-mines__table')) }
    })
    assert.ok(measure.documentWidth <= viewport.width, `${viewport.name}: document horizontal overflow`)
    assert.ok(measure.table.x >= -1 && measure.table.right <= viewport.width + 1, `${viewport.name}: table clips horizontally`)
    assert.ok(measure.board.x >= 0 && measure.board.right <= viewport.width + 1, `${viewport.name}: board clips horizontally`)
    const minimumTile = viewport.name.startsWith('portrait') ? 44 : 32
    assert.ok(measure.firstTile.width >= minimumTile && measure.firstTile.height >= minimumTile, `${viewport.name}: tiles below ${minimumTile}px touch target`)
    if (viewport.name === 'desktop') {
      assert.ok(measure.documentHeight <= viewport.height, '1440x900: document must not scroll')
      assert.ok(!measure.bodyScroll || measure.bodyScroll.scroll <= measure.bodyScroll.client + 1, '1440x900: game body must not scroll')
    }
    await primary().scrollIntoViewIfNeeded()
    const action = await primary().boundingBox()
    assert.ok(action && action.x >= 0 && action.x + action.width <= viewport.width + 1 && action.y >= 0 && action.y + action.height <= viewport.height + 1, `${viewport.name}: primary action cannot be reached`)
    await expect(primary()).toBeEnabled()
    await page.locator('.ff-mines__board').scrollIntoViewIfNeeded()
    await screenshot(viewport.name === 'desktop' ? 'desktop-active' : viewport.name)
    viewports.push({ ...viewport, ...measure })
  }
  checks.push('1440x900 document/body without scroll; 320/360/390/430 portrait and 844/667 short landscape board/control bounds, horizontal overflow and touch target checks')

  // Native touch input, with the same live server flow, on the narrowest supported phone.
  const touchContext = await browser.newContext({ viewport: { width: 320, height: 568 }, isMobile: true, hasTouch: true })
  const touchPage = await touchContext.newPage()
  touchPage.on('pageerror', error => errors.push(error.message))
  await touchPage.goto(origin)
  await expect(touchPage.getByRole('button', { name: /^Place wager/ })).toBeEnabled()
  await touchPage.getByRole('button', { name: /^Place wager/ }).tap()
  await expect(touchPage.locator('[data-tile="0"]')).toBeEnabled()
  await touchPage.locator('[data-tile="0"]').tap()
  await expect(touchPage.locator('[data-tile="0"]')).toBeDisabled()
  await touchContext.close()
  checks.push('320px native touch context places wager and reveals a tile')

  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: 'How to play Mines' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('dialog')).toContainText('rounding can increase the effective edge above 1%')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await expect(page.getByRole('button', { name: 'How to play Mines' })).toBeFocused()
  checks.push('Rules show exact math and rounding; Escape closes native modal and returns keyboard focus')
  assert.deepEqual(errors, [], 'No uncaught browser errors')
  const report = { result: 'passed', origin, practiceOnly: true, checks, viewports, screenshots: evidence }
  await writeFile(`${evidence}e2e-report.json`, `${JSON.stringify(report, null, 2)}\n`)
  console.log(JSON.stringify(report, null, 2))
} catch (error) {
  await screenshot('e2e-failure').catch(() => undefined)
  await writeFile(`${evidence}e2e-failure.json`, `${JSON.stringify({ result: 'failed', message: error.message, checks, viewports, errors }, null, 2)}\n`)
  throw error
} finally {
  await browser.close()
}

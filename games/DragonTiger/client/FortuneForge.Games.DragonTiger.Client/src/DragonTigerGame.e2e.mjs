// Start the package's API and Vite preview, then run this file with Node.
// Optional: DRAGON_TIGER_URL, DRAGON_TIGER_BROWSER_CHANNEL, DRAGON_TIGER_SCREENSHOTS.
import assert from 'node:assert/strict'
import { mkdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { chromium, expect } from '@playwright/test'

const origin = process.env.DRAGON_TIGER_URL || 'http://127.0.0.1:5180'
const browser = await chromium.launch({ headless: true, ...(process.env.DRAGON_TIGER_BROWSER_CHANNEL ? { channel: process.env.DRAGON_TIGER_BROWSER_CHANNEL } : {}) })
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
const page = await context.newPage()
const errors = []
page.on('pageerror', error => errors.push(error.message))
const screenshots = process.env.DRAGON_TIGER_SCREENSHOTS ? fileURLToPath(new URL('./.e2e-artifacts/', import.meta.url)) : null
if (screenshots) await mkdir(screenshots, { recursive: true })
const money = value => `R${Math.abs(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const api = path => `${origin}/api/games/dragon-tiger${path}`
const readStatus = async () => {
  const response = await context.request.get(api('/status'))
  assert.equal(response.status(), 200)
  return response.json()
}
const assertSettlement = (round, before) => {
  const expectedOutcome = round.dragonValue > round.tigerValue ? 'dragon' : round.dragonValue < round.tigerValue ? 'tiger' : 'tie'
  assert.equal(round.outcome, expectedOutcome)
  let expectedReturn = 0
  for (const bet of round.bets) {
    const returned = bet.side === expectedOutcome ? bet.stake * (bet.side === 'tie' ? 9 : 2) : expectedOutcome === 'tie' && bet.side !== 'tie' ? bet.stake / 2 : 0
    assert.equal(bet.totalReturn, returned)
    assert.equal(bet.profit, returned - bet.stake)
    expectedReturn += returned
  }
  assert.equal(round.totalReturn, expectedReturn)
  assert.equal(round.profit, expectedReturn - round.totalStake)
  assert.equal(round.balance, before - round.totalStake + expectedReturn)
}
const dragon = () => page.getByRole('button', { name: /^Bet on Dragon,/ })
const tiger = () => page.getByRole('button', { name: /^Bet on Tiger,/ })
const tie = () => page.getByRole('button', { name: /^Bet on Tie,/ })
const clear = () => page.getByRole('button', { name: /Clear/ })
const deal = () => page.getByRole('button', { name: /Deal cards/ })
const total = () => page.getByTestId('dragon-tiger-total-bet')

try {
  await page.goto(origin)
  await expect(dragon()).toBeEnabled()
  await page.getByRole('button', { name: /Mute sound|Turn sound on/ }).click()
  const before = (await readStatus()).balance

  await page.getByRole('button', { name: 'R2.00 chip' }).click()
  await dragon().click(); await dragon().click(); await dragon().click()
  await expect(dragon()).toHaveAccessibleName(/current bet R6.00/)
  await expect(dragon().getByLabel('3 chips placed').locator('img')).toHaveCount(3)
  if (screenshots) await page.screenshot({ path: `${screenshots}desktop-stacks.png`, fullPage: true })
  await page.getByRole('button', { name: /Undo/ }).click()
  await expect(total()).toHaveText('R4.00')
  await clear().click()
  await expect(total()).toHaveText('R0.00')

  await page.getByRole('button', { name: 'R1.00 chip' }).click()
  await dragon().click(); await tiger().click()
  await page.getByRole('button', { name: 'R0.50 chip' }).click(); await tie().click()
  const responsePromise = page.waitForResponse(response => response.url().endsWith('/api/games/dragon-tiger/rounds') && response.request().method() === 'POST')
  await deal().click()
  const response = await responsePromise
  assert.equal(response.status(), 201)
  const submitted = response.request()
  const originalKey = submitted.headers()['idempotency-key']
  assert.match(originalKey, /^dragon-tiger-/)
  const round = await response.json()
  assertSettlement(round, before)
  assert.deepEqual(submitted.postDataJSON(), { dragonStake: 1, tigerStake: 1, tieStake: .5 })
  await expect(page.getByTestId('dragon-tiger-balance')).toHaveText(money(round.balance))
  await expect(page.getByTestId('dragon-tiger-win')).toHaveText(money(Math.max(0, round.profit)))
  await expect(page.getByRole('button', { name: /Rebet/ })).toBeEnabled()
  assert.equal(await page.evaluate(() => localStorage.getItem('fortuneforge:dragon-tiger:pending:standalone-practice')), null)
  if (screenshots) await page.screenshot({ path: `${screenshots}desktop-result.png`, fullPage: true })

  const replay = await context.request.post(api('/rounds'), { headers: { 'Idempotency-Key': originalKey }, data: submitted.postDataJSON() })
  assert.equal(replay.status(), 201)
  assert.deepEqual(await replay.json(), round)
  assert.equal((await readStatus()).balance, round.balance)
  const changed = await context.request.post(api('/rounds'), { headers: { 'Idempotency-Key': originalKey }, data: { dragonStake: 2, tigerStake: 0, tieStake: 0 } })
  assert.equal(changed.status(), 409)
  assert.equal((await changed.json()).code, 'dragon-tiger-round-conflict')
  assert.equal((await readStatus()).balance, round.balance)

  // Lose the HTTP response only after the real server has settled the wager.
  await clear().click()
  await page.getByRole('button', { name: 'R1.00 chip' }).click(); await dragon().click()
  const beforeRecovery = (await readStatus()).balance
  let lostRound, lostKey
  await page.route('**/api/games/dragon-tiger/rounds', async route => {
    lostKey = route.request().headers()['idempotency-key']
    const serverResponse = await route.fetch()
    assert.equal(serverResponse.status(), 201)
    lostRound = await serverResponse.json()
    await route.abort('failed')
  })
  await deal().click()
  await expect(page.getByRole('button', { name: /Restore deal/ })).toBeEnabled()
  await expect(tiger()).toBeDisabled()
  assertSettlement(lostRound, beforeRecovery)
  const pending = await page.evaluate(() => JSON.parse(localStorage.getItem('fortuneforge:dragon-tiger:pending:standalone-practice')))
  assert.equal(pending.key, lostKey)
  await page.unroute('**/api/games/dragon-tiger/rounds')
  const restoredResponse = page.waitForResponse(value => value.url().endsWith('/api/games/dragon-tiger/rounds') && value.request().method() === 'POST')
  await page.reload()
  const restored = await restoredResponse
  assert.equal(restored.request().headers()['idempotency-key'], lostKey)
  assert.deepEqual(await restored.json(), lostRound)
  await expect(page.getByTestId('dragon-tiger-balance')).toHaveText(money(lostRound.balance))
  await expect(dragon()).toBeEnabled()
  assert.equal((await readStatus()).balance, lostRound.balance)
  assert.equal(await page.evaluate(() => localStorage.getItem('fortuneforge:dragon-tiger:pending:standalone-practice')), null)

  const viewports = [{ name: 'desktop', width: 1440, height: 1000 }, { name: 'portrait', width: 390, height: 844 }, { name: 'landscape', width: 844, height: 390 }, { name: 'small-portrait', width: 320, height: 640 }]
  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${viewport.name}: horizontal result overflow`)
    if (screenshots && viewport.name !== 'desktop') await page.screenshot({ path: `${screenshots}${viewport.name}-result.png`, fullPage: true })
  }
  await clear().click()
  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await expect(dragon()).toBeVisible()
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${viewport.name}: horizontal page overflow`)
    for (const spot of [dragon(), tiger(), tie()]) {
      const box = await spot.boundingBox()
      assert.ok(box && box.x >= 0 && box.x + box.width <= viewport.width + 1 && box.height >= 44, `${viewport.name}: betting spot has an unusable size or clips offscreen`)
    }
    await page.getByRole('button', { name: 'R0.50 chip' }).click(); await dragon().click()
    await expect(total()).toHaveText('R0.50')
    await expect(deal()).toBeEnabled()
    await clear().click()
    if (screenshots) await page.screenshot({ path: `${screenshots}${viewport.name}.png`, fullPage: true })
  }
  await page.getByRole('button', { name: /How to play/ }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('dialog')).toContainText('Half your bet returned')
  await page.getByRole('button', { name: 'Close rules' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ result: 'passed', origin, firstOutcome: round.outcome, recoveryOutcome: lostRound.outcome, checks: ['real mixed-spot wager and settlement', 'same-key replay does not debit twice', 'changed wager key conflict', 'response loss and reload recovery', 'visible repeated chip stack', 'undo and clear', 'desktop and mobile portrait/landscape including 320px', 'guide dialog', 'no browser errors'], ...(screenshots ? { screenshots } : {}) }, null, 2))
} finally {
  await browser.close()
}

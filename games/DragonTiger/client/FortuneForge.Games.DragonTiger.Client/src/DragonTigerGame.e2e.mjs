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
const chooseChip = async value => {
  const result = page.getByRole('dialog', { name: 'Round result' })
  if (await result.count()) {
    // The first outside click only acknowledges the result, not its underlying control.
    await page.getByRole('button', { name: /^Choose chip value, currently R/ }).click()
    await expect(result).toHaveCount(0)
    await expect(page.getByRole('group', { name: 'Choose chip value' })).toHaveCount(0)
    await expect(total()).toHaveText('R0.00')
    await expect(page.getByLabel('Face down card', { exact: true })).toHaveCount(2)
  }
  await page.getByRole('button', { name: /^Choose chip value, currently R/ }).click()
  await page.getByRole('button', { name: `R${value.toFixed(2)} chip` }).click()
}
const takeBack = side => page.getByRole('button', { name: `Take back last chip from ${side} bet` })
const returnDraftChips = async () => {
  for (const side of ['Dragon', 'Tiger', 'Tie']) {
    let returned = 0
    while (await takeBack(side).count()) {
      assert.ok(returned++ < 1000, `${side}: taking back a chip did not empty the draft`)
      await expect(takeBack(side)).toBeEnabled()
      await takeBack(side).click()
    }
  }
}
const deal = () => page.getByRole('button', { name: /Deal cards/ })
const total = () => page.getByTestId('dragon-tiger-total-bet')

try {
  await page.goto(origin)
  await expect(dragon()).toBeEnabled()
  await expect(page.getByRole('complementary', { name: 'Recent results' })).toHaveCount(0)
  await page.getByRole('button', { name: /Mute sound|Turn sound on/ }).click()
  const before = (await readStatus()).balance

  await chooseChip(2)
  await dragon().click(); await dragon().click(); await dragon().click()
  await expect(dragon()).toHaveAccessibleName(/current bet R6.00/)
  await expect(dragon().locator('..').getByLabel('3 chips placed').locator('img')).toHaveCount(3)
  await expect(dragon().locator('button')).toHaveCount(0)
  if (screenshots) await page.screenshot({ path: `${screenshots}desktop-stacks.png`, fullPage: true })
  await takeBack('Dragon').click()
  await expect(total()).toHaveText('R4.00')
  await returnDraftChips()
  await expect(total()).toHaveText('R0.00')
  await expect(page.getByRole('button', { name: /Undo|Clear/ })).toHaveCount(0)

  await chooseChip(1)
  await dragon().click(); await tiger().click()
  await chooseChip(.5); await tie().click()
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
  await expect(page.getByRole('complementary', { name: 'Recent results' })).toHaveCount(0)
  const resultBox = page.getByRole('dialog', { name: 'Round result' })
  await expect(resultBox).toContainText(round.profit > 0 ? 'YOU WON' : round.profit < 0 ? 'HOUSE WINS' : 'BREAK EVEN')
  await expect(resultBox).toContainText(`${round.profit < 0 ? '−' : ''}${money(round.profit)}`)
  await expect(page.getByRole('button', { name: /Rebet/ })).toBeEnabled()
  await expect(takeBack('Dragon')).toBeDisabled()
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
  await chooseChip(1); await dragon().click()
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

  const viewports = [{ name: 'desktop', width: 1440, height: 1000 }, { name: 'zoom-sized-desktop', width: 1280, height: 686 }, { name: 'large-desktop', width: 1920, height: 1030 }, { name: 'portrait', width: 390, height: 844 }, { name: 'landscape', width: 844, height: 390 }, { name: 'small-portrait', width: 320, height: 640 }]
  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${viewport.name}: horizontal result overflow`)
    if (screenshots && viewport.name !== 'desktop') await page.screenshot({ path: `${screenshots}${viewport.name}-result.png`, fullPage: true })
  }
  // A new chip starts a fresh draft; settled bets cannot be taken back.
  await chooseChip(1); await dragon().click()
  await returnDraftChips()
  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await expect(dragon()).toBeVisible()
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${viewport.name}: horizontal page overflow`)
    const layout = await page.evaluate(() => {
      const element = selector => {
        const node = document.querySelector(selector)
        if (!node) throw new Error(`Missing layout element: ${selector}`)
        return node
      }
      const rect = selector => {
        const { top, right, bottom, left, width, height } = element(selector).getBoundingClientRect()
        return { top, right, bottom, left, width, height }
      }
      const controls = element('.ff-dt__betting-controls')
      const wallet = element('.ff-dt__wallet')
      const arenaStyle = getComputedStyle(element('.ff-dt__arena'))
      return {
        table: rect('.ff-dt__table'), controls: rect('.ff-dt__betting-controls'),
        wallet: rect('.ff-dt__wallet'), selector: rect('.ff-dt__chip-selector'),
        deal: rect('.ff-dt__actions > .ff-dt__deal'), bets: rect('.ff-dt__bets'),
        arena: rect('.ff-dt__arena'), card: rect('.ff-dt__card-wrap'),
        arenaPaddingY: parseFloat(arenaStyle.paddingTop) + parseFloat(arenaStyle.paddingBottom),
        feedbackHeight: rect('.ff-dt__feedback').height,
        controlBackground: getComputedStyle(controls).backgroundColor,
        controlBackgroundImage: getComputedStyle(controls).backgroundImage,
        walletBackground: getComputedStyle(wallet).backgroundColor,
        selectorButtons: [...element('.ff-dt__chip-selector').querySelectorAll(':scope > button')].map(button => {
          const { top, bottom, width, height } = button.getBoundingClientRect()
          return { top, bottom, width, height }
        }),
        pageHeight: Math.max(document.documentElement.scrollHeight, document.body.scrollHeight),
      }
    })
    assert.ok(layout.pageHeight <= viewport.height + 1 && layout.table.bottom <= viewport.height + 1, `${viewport.name}: vertical page overflow`)
    assert.ok(Math.abs(layout.wallet.bottom - (layout.table.bottom - 1)) <= 1, `${viewport.name}: wallet is not flush with the bottom of the table`)
    assert.equal(layout.feedbackHeight, 0, `${viewport.name}: empty feedback reserves a bottom-bar row`)
    assert.equal(layout.controlBackgroundImage, 'none', `${viewport.name}: control console has an oversized tinted background`)
    assert.equal(layout.controlBackground, 'rgba(0, 0, 0, 0)', `${viewport.name}: control console is not transparent`)
    assert.notEqual(layout.walletBackground, 'rgba(0, 0, 0, 0)', `${viewport.name}: actual bottom wallet lost its tint`)
    assert.ok(layout.deal.top >= layout.controls.top - 1 && layout.deal.bottom <= layout.controls.bottom + 1 && layout.deal.bottom <= layout.wallet.top + 1, `${viewport.name}: Deal intrudes into the wallet or escapes its row`)
    assert.ok(layout.selectorButtons.every(button => button.width >= 44 && button.height >= 44), `${viewport.name}: chip selector has undersized touch targets`)
    assert.ok(layout.selectorButtons.every((button, index, buttons) => index === 0 || button.top >= buttons[index - 1].bottom - 1), `${viewport.name}: chip selector targets overlap`)
    const selectorOverlapsBets = layout.selector.left < layout.bets.right && layout.selector.right > layout.bets.left && layout.selector.top < layout.bets.bottom && layout.selector.bottom > layout.bets.top
    assert.equal(selectorOverlapsBets, false, `${viewport.name}: chip selector covers a betting spot`)
    if (viewport.height > 480 || viewport.width <= 540) {
      // Short landscape intentionally places the selector above the console in its left gutter.
      assert.ok(layout.selector.top >= layout.controls.top - 1 && layout.selector.bottom <= layout.controls.bottom + 1, `${viewport.name}: chip selector escapes its reserved control zone`)
    }
    if (viewport.width > 540 && viewport.height > 480) {
      assert.ok(Math.abs(layout.controls.height - 132) <= 1, `${viewport.name}: control console expanded beyond its 132px touch-target budget`)
      assert.ok(layout.card.height >= Math.min(210, Math.max(0, layout.arena.height - layout.arenaPaddingY - 39)) - 1, `${viewport.name}: cards are needlessly small for the available arena`)
    }
    for (const spot of [dragon(), tiger(), tie()]) {
      const box = await spot.boundingBox()
      assert.ok(box && box.x >= 0 && box.x + box.width <= viewport.width + 1 && box.height >= 44, `${viewport.name}: betting spot has an unusable size or clips offscreen`)
    }
    await chooseChip(.5); await dragon().click()
    await expect(total()).toHaveText('R0.50')
    await expect(deal()).toBeEnabled()
    await returnDraftChips()
    if (screenshots) await page.screenshot({ path: `${screenshots}${viewport.name}.png`, fullPage: true })
  }
  await page.getByRole('button', { name: /How to play/ }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('dialog')).toContainText('Half your bet returned')
  await page.getByRole('button', { name: 'Close rules' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ result: 'passed', origin, firstOutcome: round.outcome, recoveryOutcome: lostRound.outcome, checks: ['real mixed-spot wager and settlement', 'net win/loss/break-even result popup', 'outside result click resets without activating underlying control', 'same-key replay does not debit twice', 'changed wager key conflict', 'response loss and reload recovery', 'visible repeated chip stack', 'per-spot draft chip returns without nested buttons', 'settled wagers cannot be taken back', 'desktop and mobile portrait/landscape including 320px', 'guide dialog', 'no browser errors'], ...(screenshots ? { screenshots } : {}) }, null, 2))
} finally {
  await browser.close()
}

import { mkdir, copyFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { expect, test, type Page, type TestInfo } from '@playwright/test'
import type { LiarsDiceBid, LiarsDiceMatch, LiarsDicePlayer, LiarsDiceStatus } from '../../../games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client/src/contracts'

const player = 'liars-dice-ux-player'
const status: LiarsDiceStatus = { available: true, startingDicePerPlayer: 5, mode: 'free-play-bots' }
const storageKey = `fortuneforge:liars-dice:match:${player}:${status.mode}`
const id = '00000000-0000-4000-8000-000000000001'
const viewports = [
  { name: 'desktop', width: 1280, height: 720 }, { name: 'phone', width: 390, height: 844 },
  { name: 'short-phone', width: 390, height: 700 }, { name: 'compact-phone', width: 320, height: 568 },
  { name: 'narrow-landscape', width: 500, height: 320 }, { name: 'small-landscape', width: 568, height: 320 },
  { name: 'landscape', width: 667, height: 375 }, { name: 'wide-landscape', width: 852, height: 393 },
] as const

for (const viewport of viewports) test(`Liar's Dice ${viewport.name} keeps the table and actions stable through two resolved rounds`, async ({ page }, info) => {
  await page.setViewportSize(viewport)
  const fixture = await mockLiarsDice(page)
  await page.goto('/games/liars-dice')
  await expect(primary(page)).toHaveText('New match')
  await expect(primary(page)).toBeEnabled()
  await page.evaluate(() => document.fonts.ready)
  expect(fixture.writes).toHaveLength(0)
  await fits(page)
  await capture(page, info, viewport.name + '-idle')
  const table = await page.locator('.ff-liars-table').boundingBox()
  const actions = await primary(page).boundingBox()

  await page.getByRole('button', { name: 'Setup', exact: true }).click()
  await page.getByRole('combobox', { name: 'New match starting dice' }).selectOption('3')
  await fitsPanel(page)
  await capture(page, info, viewport.name + '-setup')
  await page.getByRole('button', { name: 'Start new match', exact: true }).click()
  await expect(primary(page)).toHaveText('Place bid')
  await expect(primary(page)).toBeEnabled()
  expect(fixture.writes[0].body).toEqual({ dicePerPlayer: 3 })
  await expect(page.getByRole('img', { name: 'Your private dice: 1, 4, 6', exact: true })).toBeVisible()
  await expect(page.locator('.ff-liars-player .ff-dice-throw')).toHaveCount(0)
  await page.getByRole('spinbutton', { name: 'Bid quantity' }).fill('2')
  await page.getByRole('button', { name: 'Bid face 4', exact: true }).click()
  await primary(page).click()
  await expect(page.locator('.ff-liars-bid-value')).toHaveAttribute('aria-label', '3 × 4s')
  expect(fixture.writes.at(-1)?.body).toEqual({ quantity: 2, face: 4 })
  await fits(page)
  await capture(page, info, viewport.name + '-bid')

  await page.getByRole('button', { name: 'Call liar', exact: true }).click()
  await expect(primary(page)).toHaveText('Next round')
  await expect(page.getByRole('status')).toContainText('Amber Badger loses a die')
  await expect(page.locator('.ff-liars-player.seat-1')).toHaveClass(/lost-die/)
  await expect(page.locator('.ff-liars-player.seat-1 .ff-liars-dice-count')).toHaveAttribute('aria-label', 'Amber Badger: 2 dice')
  await expect(page.getByRole('region', { name: 'Your last cup', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Call liar', exact: true })).toBeDisabled()
  await fits(page)
  expect(await page.locator('.ff-liars-table').boundingBox()).toEqual(table)
  expect(await primary(page).boundingBox()).toEqual(actions)
  await capture(page, info, viewport.name + '-liar-result')

  await primary(page).click()
  await expect(primary(page)).toHaveText('Place bid')
  await expect(primary(page)).toBeEnabled()
  await expect(page.locator('.ff-liars-round')).toHaveText('Round 2 · 11 dice')
  await expect(page.getByRole('img', { name: 'Your private dice: 6, 6, 2', exact: true })).toBeVisible()
  await expect(page.locator('.ff-liars-bid-value')).toHaveAttribute('aria-label', '2 × 6s')
  expect(fixture.writes.at(-1)?.body).toEqual({})
  await fits(page)
  await capture(page, info, viewport.name + '-next-round')
  await page.getByRole('button', { name: 'Spot on', exact: true }).click()
  await expect(primary(page)).toHaveText('Next round')
  await expect(page.getByRole('status')).toContainText('Spot On')
  await expect(page.getByRole('status')).toContainText('2 matching 6s')
  await expect(page.getByRole('status')).toContainText('Silver Otter loses a die')
  await fits(page)
  expect(await primary(page).boundingBox()).toEqual(actions)
  await capture(page, info, viewport.name + '-spot-on-result')
  await page.getByRole('button', { name: 'History', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'History' }).locator('li')).toHaveCount(2)
  await expect(page.getByRole('dialog', { name: 'History' })).toContainText('Round 2')
  await expect(page.getByRole('dialog', { name: 'History' })).toContainText('Round 1')
  await fitsPanel(page)
  await capture(page, info, viewport.name + '-history')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: 'History', exact: true })).toBeFocused()
})

test("Liar's Dice maximum bid has no illegal raise and leaves both calls available", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await savedMatch(page, fixture, bidding({ bid: { quantity: 20, face: 6 }, bidder: 'bot-1' }))
  await page.goto('/games/liars-dice')
  await expect(primary(page)).toHaveText('Maximum bid')
  await expect(primary(page)).toBeDisabled()
  await expect(page.getByRole('spinbutton', { name: 'Bid quantity' })).toHaveValue('')
  await expect(page.getByRole('button', { name: 'Call liar', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Spot on', exact: true })).toBeEnabled()
  await page.getByRole('spinbutton', { name: 'Bid quantity' }).fill('20')
  for (let face = 1; face <= 6; face++) {
    await page.getByRole('button', { name: `Bid face ${face}`, exact: true }).click()
    await expect(primary(page)).toBeDisabled()
  }
  expect(fixture.writes).toHaveLength(0)
  await fits(page)
  await capture(page, info, 'maximum-bid')
  await page.getByRole('button', { name: 'Call liar', exact: true }).click()
  await expect(primary(page)).toHaveText('Next round')
  expect(fixture.writes.map(request => request.operation)).toEqual(['challenge'])
})

test("Liar's Dice exact drafts and disabled status never create automatic matches or coerced bids", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await page.goto('/games/liars-dice')
  await expect(primary(page)).toBeEnabled()
  expect(fixture.writes).toHaveLength(0)
  await primary(page).click()
  await expect(primary(page)).toHaveText('Place bid')
  for (const quantity of ['', '0', '-1', '1.5', '21']) {
    await page.getByRole('spinbutton', { name: 'Bid quantity' }).fill(quantity)
    await expect(primary(page)).toBeDisabled()
  }
  expect(fixture.writes.map(request => request.operation)).toEqual(['start'])
  await page.getByRole('spinbutton', { name: 'Bid quantity' }).fill('2')
  await page.getByRole('button', { name: 'Bid face 1', exact: true }).click()
  await primary(page).click()
  await expect(page.locator('.ff-liars-bid-value')).toHaveAttribute('aria-label', '3 × 4s')
  expect(fixture.writes.at(-1)?.body).toEqual({ quantity: 2, face: 1 })
  await page.getByRole('spinbutton', { name: 'Bid quantity' }).fill('3')
  await page.getByRole('button', { name: 'Bid face 4', exact: true }).click()
  await expect(primary(page)).toBeDisabled()
  await capture(page, info, 'invalid-equal-bid')
  fixture.tableStatus.available = false
  await page.reload()
  await expect(primary(page)).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Call liar', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Spot on', exact: true })).toBeDisabled()
  await expect(page.getByRole('alert')).toContainText('temporarily unavailable')
  expect(fixture.writes).toHaveLength(2)
  await capture(page, info, 'table-unavailable')
})

test("Liar's Dice accepted uncertain bid, call and next round use GET without replay and restore on reload", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await page.goto('/games/liars-dice')
  await primary(page).click()
  await expect(primary(page)).toHaveText('Place bid')
  fixture.failNext = 'bid'
  await primary(page).click()
  await expect(page.getByRole('alert')).toContainText('Match refreshed')
  await expect(page.locator('.ff-liars-bid-value')).toHaveAttribute('aria-label', '3 × 4s')
  expect(fixture.writes.filter(request => request.operation === 'bid')).toHaveLength(1)
  await capture(page, info, 'lost-bid-restored')
  fixture.failNext = 'challenge'
  await page.getByRole('button', { name: 'Call liar', exact: true }).click()
  await expect(primary(page)).toHaveText('Next round')
  expect(fixture.writes.filter(request => request.operation === 'challenge')).toHaveLength(1)
  await capture(page, info, 'lost-call-restored')
  fixture.failNext = 'next-round'
  await primary(page).click()
  await expect(page.locator('.ff-liars-round')).toHaveText('Round 2 · 19 dice')
  await expect(primary(page)).toBeEnabled()
  expect(fixture.writes.filter(request => request.operation === 'next-round')).toHaveLength(1)
  expect(fixture.reads).toHaveLength(3)
  await capture(page, info, 'lost-next-round-restored')
  const writes = fixture.writes.length
  await page.reload()
  await expect(page.locator('.ff-liars-round')).toHaveText('Round 2 · 19 dice')
  await expect(primary(page)).toBeEnabled()
  expect(fixture.writes).toHaveLength(writes)
  expect(fixture.reads).toHaveLength(4)
  await page.getByRole('button', { name: 'Spot on', exact: true }).click()
  await expect(primary(page)).toHaveText('Next round')
  await page.reload()
  await expect(primary(page)).toHaveText('Next round')
  await page.getByRole('button', { name: 'History', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'History' }).locator('li')).toHaveCount(1)
  await expect(page.getByRole('dialog', { name: 'History' })).toContainText('Round 2')
  await capture(page, info, 'reload-resolved-history')
})

test("Liar's Dice failed read locks all play until GET recovery and a missing match expires", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await page.goto('/games/liars-dice')
  await primary(page).click()
  await expect(primary(page)).toHaveText('Place bid')
  fixture.failNext = 'bid'; fixture.failReads = true
  await primary(page).click()
  await expect(page.getByRole('alert')).toContainText('not restored')
  await expect(primary(page)).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Call liar', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Spot on', exact: true })).toBeDisabled()
  await expect(page.getByRole('spinbutton', { name: 'Bid quantity' })).toBeDisabled()
  await page.getByRole('button', { name: 'Setup', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Start new match', exact: true })).toBeDisabled()
  await page.keyboard.press('Escape')
  expect(fixture.writes).toHaveLength(2)
  await capture(page, info, 'restoration-locked')
  for (const viewport of [{ width: 320, height: 568 }, { width: 500, height: 320 }]) {
    await page.setViewportSize(viewport)
    await fits(page); await errorFits(page)
    await capture(page, info, 'restoration-locked-' + viewport.width + 'x' + viewport.height)
  }
  fixture.failReads = false
  await page.getByRole('button', { name: 'Check again', exact: true }).click()
  await expect(primary(page)).toBeEnabled()
  await expect(page.locator('.ff-liars-bid-value')).toHaveAttribute('aria-label', '3 × 4s')
  expect(fixture.writes).toHaveLength(2)
  await capture(page, info, 'restoration-retried')
  fixture.match = null
  await page.reload()
  await expect(primary(page)).toHaveText('New match')
  await expect(primary(page)).toBeEnabled()
  await expect(page.getByRole('alert')).toContainText('expired')
  expect(await page.evaluate(key => sessionStorage.getItem(key), storageKey)).toBeNull()
  await capture(page, info, 'expired-match')
  for (const viewport of [{ width: 320, height: 568 }, { width: 500, height: 320 }]) {
    await page.setViewportSize(viewport)
    await fits(page); await errorFits(page)
    await capture(page, info, 'expired-match-' + viewport.width + 'x' + viewport.height)
  }
})

test("Liar's Dice lost new-match response does not restore or replay an unrelated old match", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  const old = resolvedCall(bidding({ counts: [1, 1, 0, 0], bid: { quantity: 1, face: 4 }, bidder: 'bot-1', hand: [1] }), 'liar', 0)
  await savedMatch(page, fixture, old)
  await page.goto('/games/liars-dice')
  await expect(primary(page)).toHaveText('Rematch')
  fixture.failNext = 'start'
  await primary(page).click()
  await expect(page.getByRole('alert')).toContainText('Match response lost')
  await expect(primary(page)).toHaveText('New match')
  await expect(primary(page)).toBeEnabled()
  expect(fixture.writes.filter(request => request.operation === 'start')).toHaveLength(1)
  expect(fixture.reads).toEqual([id])
  expect(await page.evaluate(key => sessionStorage.getItem(key), storageKey)).toBeNull()
  await capture(page, info, 'lost-new-match-unknown')
})

test("Liar's Dice eliminated human keeps the historical last cup, then watches server-controlled turns", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await savedMatch(page, fixture, bidding({ counts: [1, 2, 2, 0], hand: [4], bid: { quantity: 1, face: 4 }, bidder: 'bot-1' }))
  fixture.matchingDice = 1
  await page.goto('/games/liars-dice')
  await page.getByRole('button', { name: 'Call liar', exact: true }).click()
  await expect(primary(page)).toHaveText('Next round')
  await expect(page.locator('.ff-liars-player.is-human')).toHaveClass(/is-out/)
  await expect(page.locator('.ff-liars-player.is-human .ff-liars-dice-count')).toHaveAttribute('aria-label', 'You: 0 dice')
  await expect(page.getByRole('region', { name: 'Your last cup', exact: true })).toBeVisible()
  await expect(page.getByRole('img', { name: 'Your private dice: 4', exact: true })).toBeVisible()
  expect(fixture.match?.totalDice).toBe(5)
  expect(fixture.match?.players.reduce((sum, seat) => sum + seat.diceCount, 0)).toBe(4)
  await capture(page, info, 'human-eliminated-last-cup')
  await primary(page).click()
  await expect(page.getByText('No dice remaining', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Call liar', exact: true })).toBeDisabled()
  await expect(page.getByRole('spinbutton', { name: 'Bid quantity' })).toBeDisabled()
  await expect(page.locator('.ff-liars-hand [data-face]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Continue', exact: true })).toHaveCount(0)
  await expect(primary(page)).toHaveText('Next round')
  expect(fixture.writes.map(request => request.operation)).toEqual(['challenge', 'next-round', 'advance'])
  await capture(page, info, 'eliminated-spectator-server-turn')
})

test("Liar's Dice server winner ends play and rematch clears the old outcome/history", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await savedMatch(page, fixture, bidding({ counts: [1, 1, 0, 0], hand: [1], bid: { quantity: 1, face: 4 }, bidder: 'bot-1' }))
  fixture.matchingDice = 0
  await page.goto('/games/liars-dice')
  await page.getByRole('button', { name: 'Call liar', exact: true }).click()
  await expect(primary(page)).toHaveText('Rematch')
  await expect(page.getByRole('status')).toContainText('You win the match')
  await expect(page.locator('.ff-liars-player.is-human')).toHaveClass(/is-winner/)
  await expect(page.getByRole('button', { name: 'Call liar', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Spot on', exact: true })).toBeDisabled()
  await capture(page, info, 'match-winner')
  const oldId = fixture.match?.matchId
  await primary(page).click()
  await expect(primary(page)).toHaveText('Place bid')
  expect(fixture.match?.matchId).not.toBe(oldId)
  await expect(page.locator('.ff-liars-player.is-winner')).toHaveCount(0)
  await page.getByRole('button', { name: 'History', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'History' })).toContainText('No completed rounds.')
  expect(fixture.writes.at(-1)?.body).toEqual({ dicePerPlayer: 5 })
  await capture(page, info, 'rematch-reset')
})

test("Liar's Dice inexact Spot On loses one human die without fabricating a revealed opponent cup", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await savedMatch(page, fixture, bidding({ bid: { quantity: 2, face: 4 }, bidder: 'bot-1' }))
  fixture.matchingDice = 3
  await page.goto('/games/liars-dice')
  await page.getByRole('button', { name: 'Spot on', exact: true }).click()
  await expect(primary(page)).toHaveText('Next round')
  await expect(page.getByRole('status')).toContainText('Spot On')
  await expect(page.getByRole('status')).toContainText('3 matching 4s')
  await expect(page.getByRole('status')).toContainText('You lose a die')
  await expect(page.locator('.ff-liars-player.is-human .ff-liars-dice-count')).toHaveAttribute('aria-label', 'You: 4 dice')
  await expect(page.locator('.ff-liars-hand [data-face]')).toHaveCount(5)
  await expect(page.locator('.ff-liars-player [data-face]')).toHaveCount(0)
  expect(fixture.match?.totalDice).toBe(20)
  expect(fixture.match?.players.reduce((sum, seat) => sum + seat.diceCount, 0)).toBe(19)
  await capture(page, info, 'spot-on-inexact-human-loss')
})

test("Liar's Dice normal-motion pending private cup locks the turn until its 890ms reveal", async ({ page }, info) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  const fixture = await mockLiarsDice(page)
  await page.goto('/games/liars-dice')
  await expect(primary(page)).toBeEnabled()
  await page.evaluate(() => document.fonts.ready)
  const time = new Date('2026-10-01T12:00:00Z')
  await page.clock.install({ time })
  await page.clock.pauseAt(time)
  const held = deferred()
  fixture.startGate = held.promise
  await primary(page).click()
  await expect(primary(page)).toBeDisabled()
  await expect(page.getByRole('img', { name: 'Rolling your private cup', exact: true })).toHaveClass(/is-rolling/)
  await expect(page.locator('.ff-liars-hand [data-face]')).toHaveCount(0)
  await capture(page, info, 'normal-motion-start-waiting')
  held.resolve()
  await expect(page.getByRole('img', { name: 'Rolling your private cup', exact: true })).toHaveClass(/has-result/)
  await expect(page.locator('.ff-liars-hand [data-face]')).toHaveCount(5)
  await expect(page.locator('.ff-liars-hand .ff-die-sprite-frame').first()).toHaveCSS('animation-name', 'ff-die-course')
  await expect(primary(page)).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Call liar', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Hide dice', exact: true })).toBeDisabled()
  await capture(page, info, 'normal-motion-pending-private-cup')
  await page.clock.runFor(889)
  await expect(primary(page)).toBeDisabled()
  await page.clock.runFor(1)
  await expect(primary(page)).toHaveText('Place bid')
  await expect(primary(page)).toBeEnabled()
  await expect(page.getByRole('img', { name: 'Your private dice: 1, 2, 3, 4, 6', exact: true })).toBeVisible()
  expect(fixture.writes.map(request => request.operation)).toEqual(['start'])
  await capture(page, info, 'normal-motion-revealed')
})

test("Liar's Dice keyboard activation and nonmodal panel Escape restore focus without trapping play", async ({ page }, info) => {
  const fixture = await mockLiarsDice(page)
  await page.goto('/games/liars-dice')
  await expect(primary(page)).toBeEnabled()
  await primary(page).focus(); await page.keyboard.press('Enter')
  await expect(primary(page)).toHaveText('Place bid')
  await page.getByRole('button', { name: 'Bid face 5', exact: true }).focus()
  await page.keyboard.press('Space')
  await expect(page.getByRole('button', { name: 'Bid face 5', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByRole('button', { name: 'Bid face 5', exact: true })).toBeFocused()
  await primary(page).focus(); await page.keyboard.press('Enter')
  await expect(page.locator('.ff-liars-bid-value')).toHaveAttribute('aria-label', '3 × 4s')
  await expect(primary(page)).toBeFocused()
  expect(fixture.writes.at(-1)?.body).toEqual({ quantity: 1, face: 5 })
  await page.getByRole('button', { name: 'Rules', exact: true }).focus(); await page.keyboard.press('Enter')
  await expect(page.getByRole('button', { name: 'Close panel' })).toBeFocused()
  await expect(page.getByRole('dialog', { name: 'Rules' })).not.toHaveAttribute('aria-modal', 'true')
  await expect(page.getByRole('dialog', { name: 'Rules' })).toContainText('ones are ordinary dice')
  await expect(page.getByRole('dialog', { name: 'Rules' })).toContainText('Opponents’ cups stay private')
  await fitsPanel(page)
  await capture(page, info, 'keyboard-rules-panel')
  await page.getByRole('spinbutton', { name: 'Bid quantity' }).focus()
  await expect(page.getByRole('spinbutton', { name: 'Bid quantity' })).toBeFocused()
  await page.getByRole('button', { name: 'Close panel' }).focus(); await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Rules', exact: true })).toBeFocused()
  await expect(page.locator('.ff-liars-table')).toHaveCSS('user-select', 'none')
  await capture(page, info, 'keyboard-focus-restored')
})

test("Liar's Dice touch selects faces, covers the private cup and calls without duplicate actions", async ({ browser }, info) => {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, reducedMotion: 'reduce' })
  const page = await context.newPage()
  const fixture = await mockLiarsDice(page)
  try {
    await page.goto('http://127.0.0.1:4177/games/liars-dice')
    await primary(page).tap()
    await expect(primary(page)).toHaveText('Place bid')
    for (const viewport of [{ width: 390, height: 844 }, { width: 320, height: 568 }, { width: 500, height: 320 }]) {
      await page.setViewportSize(viewport)
      await page.getByRole('button', { name: 'Bid face 6', exact: true }).tap()
      await expect(page.getByRole('button', { name: 'Bid face 6', exact: true })).toHaveAttribute('aria-pressed', 'true')
      await page.getByRole('button', { name: 'Hide dice', exact: true }).tap()
      await expect(page.getByRole('button', { name: 'Show dice', exact: true })).toHaveAttribute('aria-pressed', 'true')
      await expect(page.locator('.ff-liars-hand [data-face]')).toHaveCount(0)
      await page.getByRole('button', { name: 'Show dice', exact: true }).tap()
      await expect(page.locator('.ff-liars-hand [data-face]')).toHaveCount(5)
      await expect(page.locator('.ff-liars-hand .ff-die-sprite-frame').first()).toHaveCSS('animation-name', 'none')
      await fits(page)
      await capture(page, info, 'touch-' + viewport.width + 'x' + viewport.height)
    }
    await primary(page).tap()
    await expect(page.locator('.ff-liars-bid-value')).toHaveAttribute('aria-label', '3 × 4s')
    await page.getByRole('button', { name: 'Call liar', exact: true }).tap()
    await expect(primary(page)).toHaveText('Next round')
    expect(fixture.writes.map(request => request.operation)).toEqual(['start', 'bid', 'challenge'])
    await capture(page, info, 'touch-call-result')
  } finally { await context.close() }
})

function primary(page: Page) { return page.locator('.ff-liars-primary') }
async function fits(page: Page) {
  const geometry = await page.evaluate(() => ({
    width: innerWidth, height: innerHeight, sw: document.documentElement.scrollWidth, sh: document.documentElement.scrollHeight,
    boxes: [...document.querySelectorAll('.ff-liars-table,.ff-liars-felt,.ff-liars-players,.ff-liars-bid-board,.ff-liars-hand-section,.ff-liars-actions,.ff-liars-action-buttons button,.ff-liars-faces button,.ff-liars-quantity input')].map(element => {
      const b = element.getBoundingClientRect(); return { x: b.x, y: b.y, right: b.right, bottom: b.bottom, width: b.width, height: b.height }
    }),
  }))
  expect(geometry.sw).toBeLessThanOrEqual(geometry.width)
  expect(geometry.sh).toBeLessThanOrEqual(geometry.height)
  for (const box of geometry.boxes) {
    expect(box.x).toBeGreaterThanOrEqual(0); expect(box.y).toBeGreaterThanOrEqual(0)
    expect(box.right).toBeLessThanOrEqual(geometry.width + .1); expect(box.bottom).toBeLessThanOrEqual(geometry.height + .1)
    expect(box.width).toBeGreaterThan(10); expect(box.height).toBeGreaterThan(10)
  }
  for (const button of await page.locator('.ff-liars-action-buttons button').all()) {
    const b = (await button.boundingBox())!
    expect(b.height).toBeGreaterThanOrEqual(40); expect(b.width).toBeGreaterThanOrEqual(40)
    expect(await button.evaluate(element => { const r = element.getBoundingClientRect(); return element.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)) })).toBe(true)
  }
}
async function fitsPanel(page: Page) {
  const box = (await page.getByRole('dialog').boundingBox())!, viewport = page.viewportSize()!, rail = (await page.locator('.ff-liars-actions').boundingBox())!
  expect(box.x).toBeGreaterThanOrEqual(0); expect(box.y).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(viewport.width); expect(box.y + box.height).toBeLessThanOrEqual(viewport.height)
  expect(box.y + box.height).toBeLessThanOrEqual(rail.y + .1)
}
async function errorFits(page: Page) {
  const viewport = page.viewportSize()!
  const music = (await page.getByRole('button', { name: /background music/ }).boundingBox())!
  for (const element of await page.locator('.ff-liars-notice [role="alert"],.ff-liars-notice button').all()) {
    const box = (await element.boundingBox())!
    expect(box.x).toBeGreaterThanOrEqual(0); expect(box.y).toBeGreaterThanOrEqual(0)
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.width); expect(box.y + box.height).toBeLessThanOrEqual(viewport.height)
    expect(box.y + box.height <= music.y || music.y + music.height <= box.y || box.x + box.width <= music.x || music.x + music.width <= box.x).toBe(true)
    expect(await element.evaluate(node => { const b = node.getBoundingClientRect(); return node.contains(document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2)) })).toBe(true)
  }
}
async function capture(page: Page, info: TestInfo, name: string) {
  const directory = resolve(process.cwd(), '../.artifacts/ux-cycle/liars-dice')
  await mkdir(directory, { recursive: true })
  const path = info.outputPath(name + '.png')
  await page.screenshot({ path })
  await copyFile(path, resolve(directory, name + '.png'))
  await info.attach(name, { path, contentType: 'image/png' })
}

type Operation = 'start' | 'bid' | 'challenge' | 'spot-on' | 'advance' | 'next-round'
type Fixture = { match: LiarsDiceMatch | null; tableStatus: LiarsDiceStatus; count: number; matchingDice: number | null; failNext: Operation | null; failReads: boolean; startGate: Promise<void> | null; writes: { operation: Operation; path: string; body: unknown }[]; reads: string[] }
async function mockLiarsDice(page: Page): Promise<Fixture> {
  const fixture: Fixture = { match: null, tableStatus: { ...status }, count: 0, matchingDice: null, failNext: null, failReads: false, startGate: null, writes: [], reads: [] }
  await page.route('**/api/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname, method = request.method()
    const json = (value: unknown, code = 200) => route.fulfill({ status: code, contentType: 'application/json', body: JSON.stringify(value) })
    if (path === '/api/accounts/me') return json({ userId: player, playerName: 'Alex', email: 'alex@example.test', createdAtUtc: '2026-01-01T00:00:00Z', balances: { slotsCredits: 1000, freeGames: 0 }, role: 'Player', slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } })
    if (path === '/api/games/liars-dice/status') return json(fixture.tableStatus)
    if (!path.startsWith('/api/games/liars-dice/')) return json({})
    if (method === 'GET') {
      const matchId = path.split('/').at(-1)!
      fixture.reads.push(matchId)
      if (fixture.failReads) return route.abort('failed')
      return fixture.match?.matchId === matchId ? json(fixture.match) : json({ code: 'liars-dice-match-not-found', message: 'Match expired' }, 404)
    }
    const operation: Operation = path.endsWith('/matches') ? 'start' : path.split('/').at(-1) as Operation
    const body: unknown = request.postDataJSON()
    fixture.writes.push({ operation, path, body })
    if (operation === 'start') {
      const options = body as { dicePerPlayer: number }
      fixture.count++
      fixture.match = bidding({ matchId: '00000000-0000-4000-8000-' + String(fixture.count).padStart(12, '0'), counts: [options.dicePerPlayer, options.dicePerPlayer, options.dicePerPlayer, options.dicePerPlayer] })
      if (fixture.startGate) await fixture.startGate
    } else {
      const match = fixture.match
      if (!match) return json({ code: 'liars-dice-match-not-found', message: 'Match expired' }, 404)
      if (operation === 'bid') {
        const draft = body as LiarsDiceBid
        if (!Number.isInteger(draft.quantity) || !Number.isInteger(draft.face) || draft.quantity < 1 || draft.quantity > match.totalDice || draft.face < 1 || draft.face > 6 || match.currentBid && !(draft.quantity > match.currentBid.quantity || draft.quantity === match.currentBid.quantity && draft.face > match.currentBid.face))
          return json({ code: 'liars-dice-invalid-action', message: 'Bid must be higher' }, 400)
        // Public snapshot after the server's opponents have acted. No opponent cups or bot strategy are modeled here.
        fixture.match = { ...match, currentPlayerId: 'you', currentBid: { quantity: 3, face: 4 }, currentBidderId: 'bot-1', opponentsThinking: false, message: 'Your turn' }
      } else if (operation === 'challenge' || operation === 'spot-on') {
        fixture.match = resolvedCall(match, operation === 'spot-on' ? 'spot-on' : 'liar', fixture.matchingDice ?? 2)
      } else if (operation === 'next-round') {
        const counts = match.players.map(seat => seat.diceCount)
        const humanActive = counts[0] > 0
        fixture.match = bidding({ matchId: match.matchId, roundNumber: match.roundNumber + 1, counts, hand: humanActive ? Array.from({ length: counts[0] }, (_, index) => [6, 6, 2, 1, 3, 4][index % 6]) : [], current: humanActive ? 'you' : 'bot-1', bid: humanActive ? { quantity: 2, face: 6 } : { quantity: 1, face: 3 }, bidder: humanActive ? 'bot-3' : 'bot-2' })
      } else if (operation === 'advance') {
        fixture.match = resolvedCall(match, 'liar', 0)
      }
    }
    if (fixture.failNext === operation) { fixture.failNext = null; return route.abort('failed') }
    return json(fixture.match)
  })
  return fixture
}

function bidding(options: { matchId?: string; counts?: readonly number[]; hand?: readonly number[]; roundNumber?: number; current?: string; bid?: LiarsDiceBid | null; bidder?: string | null } = {}): LiarsDiceMatch {
  const counts = options.counts ?? [5, 5, 5, 5]
  const names = ['You', 'Amber Badger', 'Copper Finch', 'Silver Otter']
  const players: LiarsDicePlayer[] = counts.map((diceCount, index) => ({ id: index === 0 ? 'you' : 'bot-' + index, displayName: names[index], diceCount, active: diceCount > 0, isHuman: index === 0 }))
  return { matchId: options.matchId ?? id, phase: 'bidding', roundNumber: options.roundNumber ?? 1, currentPlayerId: options.current ?? 'you', currentBid: options.bid ?? null, currentBidderId: options.bidder ?? null, totalDice: counts.reduce((sum, count) => sum + count, 0), hand: options.hand ?? (counts[0] === 3 ? [1, 4, 6] : Array.from({ length: counts[0] }, (_, index) => [1, 2, 3, 4, 6, 5][index % 6])), players, outcome: null, winner: null, opponentsThinking: (options.current ?? 'you') !== 'you', message: 'Your turn' }
}

function resolvedCall(match: LiarsDiceMatch, callType: 'liar' | 'spot-on', matchingDice: number): LiarsDiceMatch {
  const bid = match.currentBid!, bidderId = match.currentBidderId!, challengerId = match.currentPlayerId
  const loserId = callType === 'spot-on' ? matchingDice === bid.quantity ? bidderId : challengerId : matchingDice >= bid.quantity ? challengerId : bidderId
  const players = match.players.map(seat => seat.id === loserId ? { ...seat, diceCount: seat.diceCount - 1, active: seat.diceCount > 1 } : seat)
  const active = players.filter(seat => seat.active)
  // Cups and totalDice describe the played round; counts already reflect the one-die loss.
  return { ...match, phase: 'resolved', players, outcome: { challengerId, bidderId, loserId, quantity: bid.quantity, face: bid.face, matchingDice, callType }, winner: active.length === 1 ? active[0].id : null, opponentsThinking: false, message: 'Round resolved' }
}

async function savedMatch(page: Page, fixture: Fixture, match: LiarsDiceMatch) {
  fixture.match = match; fixture.count = Number(match.matchId.split('-').at(-1))
  await page.addInitScript(({ key, value }) => {
    if (!sessionStorage.getItem('liars-dice-fixture-seeded')) { sessionStorage.setItem(key, value); sessionStorage.setItem('liars-dice-fixture-seeded', 'true') }
  }, { key: storageKey, value: match.matchId })
}
function deferred() { let resolve!: () => void; const promise = new Promise<void>(done => { resolve = done }); return { promise, resolve } }

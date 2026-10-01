import { createHash } from 'node:crypto'
import { copyFile, mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'
import type { SicBoBetKind, SicBoBetRequest, SicBoRound, SicBoStatus } from '../../../game-packages/games/SicBo/client/FortuneForge.Games.SicBo.Client/src/contracts'

const player = 'sic-bo-ux-player'
const viewports = [
  { name: 'desktop', width: 1280, height: 720 }, { name: 'phone', width: 390, height: 844 },
  { name: 'short-phone', width: 390, height: 700 }, { name: 'compact-phone', width: 320, height: 568 },
  { name: 'narrow-landscape', width: 500, height: 320 }, { name: 'small-landscape', width: 568, height: 320 },
  { name: 'landscape', width: 667, height: 375 }, { name: 'wide-landscape', width: 852, height: 393 },
] as const
const selections = {
  small: bet('small'), big: bet('big'), odd: bet('odd'), even: bet('even'),
  any: bet('any-triple'), single: bet('single-number', { face: 2 }),
  double: bet('specific-double', { face: 2 }), triple: bet('specific-triple', { face: 3 }),
  total: bet('total', { total: 7 }), combination: bet('two-number-combination', { firstFace: 2, secondFace: 3 }),
}
const orderedMixed = [
  { ...selections.small, stake: 5 }, { ...selections.single, stake: 2 },
  selections.total, selections.combination, { ...selections.small, stake: 5 },
]

for (const viewport of viewports) test(`Sic Bo ${viewport.name} fits all 52 markets and keeps controls stable through roll, repeat and panels`, async ({ page }, info) => {
  await page.setViewportSize(viewport)
  const fixture = await mockSicBo(page)
  await ready(page)
  await page.evaluate(() => document.fonts.ready)
  expect(fixture.posts).toHaveLength(0)
  await fits(page)
  const tableBox = await page.locator('.ff-sic-bo__layout').boundingBox()
  const actionBox = await primary(page).boundingBox()
  await capture(page, info, viewport.name + '-empty')
  await add(page, 'Small', '5')
  await add(page, 'Single 2', '2')
  await add(page, 'Total 7', '1')
  await add(page, 'Combination 2 + 3')
  await add(page, 'Small', '5')
  await expect(cell(page, 'Small').locator('.ff-sic-bo__marker')).toHaveAttribute('aria-label', 'R10.00 staked')
  await expect(page.locator('.ff-sic-bo__summary')).toContainText('Bet R14.00')
  await fits(page)
  await capture(page, info, viewport.name + '-placed')
  await primary(page).click()
  await expect(primary(page)).toHaveText('Repeat bets')
  await expect(page.getByRole('img', { name: 'Dice 2, 2, 3', exact: true })).toBeVisible()
  await expect(page.locator('.ff-sic-bo__roll-total')).toHaveText('7Small')
  await expect(page.locator('.ff-sic-bo__return')).toContainText('Return R46.00')
  await expect(page.locator('.ff-sic-bo__return')).toContainText('+R32.00 net')
  await expect(cell(page, 'Small')).toHaveClass(/is-win/)
  expect(fixture.posts.map(item => item.bets)).toEqual([orderedMixed])
  expect(fixture.posts[0].roundId).toBe(roundId(player, fixture.posts[0].key, 'account'))
  await fits(page)
  expect(await page.locator('.ff-sic-bo__layout').boundingBox()).toEqual(tableBox)
  expect(await primary(page).boundingBox()).toEqual(actionBox)
  await capture(page, info, viewport.name + '-result')
  await primary(page).click()
  await expect(primary(page)).toHaveText('Roll dice')
  await expect(primary(page)).toBeFocused()
  await expect(page.getByRole('img', { name: 'Dice 2, 2, 3', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Undo', exact: true }).click()
  await expect(cell(page, 'Small').locator('.ff-sic-bo__marker')).toHaveAttribute('aria-label', 'R5.00 staked')
  await expect(page.locator('.ff-sic-bo__summary')).toContainText('Bet R9.00')
  expect(fixture.posts).toHaveLength(1)
  await fits(page)
  expect(await primary(page).boundingBox()).toEqual(actionBox)
  await capture(page, info, viewport.name + '-repeat-edited')
  for (const panel of ['Rules', 'Slip', 'History'] as const) {
    const opener = panelButton(page, panel)
    await opener.click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    await fitsPanel(page)
    await expect(dialog.getByRole('button', { name: 'Close table details' })).toBeFocused()
    for (let index = 0; index < 8; index++) {
      await page.keyboard.press('Tab')
      const focusState = await dialog.evaluate(node => ({ inside: node.contains(document.activeElement), activeTag: document.activeElement?.tagName, activeMarkup: document.activeElement?.outerHTML.slice(0,180) }))
      expect(focusState, JSON.stringify(focusState)).toMatchObject({ inside: true })
    }
    if (panel === 'Rules') {
      expect(await dialog.locator('.ff-sic-bo__panel-body').evaluate(node => node.scrollHeight > node.clientHeight)).toBe(true)
      await dialog.locator('.ff-sic-bo__panel-body').evaluate(node => { node.scrollTop = node.scrollHeight })
      await expect(dialog).toContainText('Profit odds')
    } else if (panel === 'Slip') await expect(dialog.locator('.ff-sic-bo__slip > li')).toHaveCount(4)
    else {
      await expect(dialog.locator('.ff-sic-bo__history > li')).toHaveCount(1)
      await dialog.locator('summary').click()
      await expect(dialog.locator('details')).toContainText('Small · R5.00 · Won · R10.00 return')
    }
    await capture(page, info, viewport.name + '-' + panel.toLowerCase())
    await page.keyboard.press('Escape')
    await expect(dialog).not.toBeVisible()
    await expect(opener).toBeFocused()
  }
  expect(fixture.posts).toHaveLength(1)
  expect(fixture.otherWrites).toEqual([])
})

test('Sic Bo sends all ten bet types in canonical selection order and preserves duplicate entries', async ({ page }, info) => {
  const fixture = await mockSicBo(page)
  await ready(page)
  for (const label of ['Small', 'Big', 'Odd', 'Even', 'Any Triple', 'Single 2', 'Double 2', 'Triple 3', 'Total 7', 'Combination 2 + 3', 'Small']) await add(page, label)
  await panelButton(page, 'Slip').click()
  await expect(page.locator('.ff-sic-bo__slip > li')).toHaveCount(11)
  await expect(page.locator('.ff-sic-bo__slip > li').last()).toContainText('Small')
  await page.keyboard.press('Escape')
  await primary(page).click()
  await expect(primary(page)).toHaveText('Repeat bets')
  expect(fixture.posts[0].bets).toEqual([...Object.values(selections), selections.small])
  expect(new Set(fixture.posts[0].bets.map(item => item.kind)).size).toBe(10)
  await panelButton(page, 'Slip').click()
  await expect(page.locator('.ff-sic-bo__slip > li')).toHaveCount(11)
  await expect(page.locator('.ff-sic-bo__slip > li').last()).toContainText('Won · R2.00 return')
  expect(fixture.posts).toHaveLength(1)
  await capture(page, info, 'all-types-ordered-settlements')
})

test('Sic Bo accepts an exact minimum-anchored decimal and leaves invalid drafts untouched', async ({ page }, info) => {
  const fixture = await mockSicBo(page)
  Object.assign(fixture.tableStatus, { minimumStake: 0.25, maximumStakePerBet: 2.25, stakeIncrement: 0.5 })
  await ready(page)
  await expect(stake(page)).toHaveValue('0.25')
  for (const draft of ['', '1e1', '0.30', '0.50', '0.251', '-0.25', '2.75']) {
    await stake(page).fill(draft)
    await expect(stake(page)).toHaveValue(draft)
    await expect(stake(page)).toHaveAttribute('aria-invalid', 'true')
    await expect(cell(page, 'Small')).toBeDisabled()
    await expect(primary(page)).toBeDisabled()
  }
  expect(fixture.posts).toHaveLength(0)
  await add(page, 'Small', '0.75')
  await expect(stake(page)).toHaveValue('0.75')
  await expect(cell(page, 'Small').locator('.ff-sic-bo__marker')).toHaveAttribute('aria-label', 'R0.75 staked')
  await primary(page).click()
  await expect(primary(page)).toHaveText('Repeat bets')
  expect(fixture.posts[0].bets).toEqual([{ ...selections.small, stake: 0.75 }])
  await expect(page.locator('.ff-sic-bo__return')).toContainText('Return R1.50')
  await capture(page, info, 'minimum-anchored-exact-stake')
})

test('Sic Bo enforces count and available funds without extra bets, and unavailable status locks play', async ({ page }, info) => {
  const fixture = await mockSicBo(page, { balance: 20 })
  await ready(page)
  for (let index = 0; index < 20; index++) await add(page, 'Small')
  await expect(cell(page, 'Even')).toBeDisabled()
  await expect(page.locator('.ff-sic-bo__notice')).toContainText('Bet limit reached')
  await expect(primary(page)).toBeEnabled()
  await page.getByRole('button', { name: 'Undo', exact: true }).click()
  await stake(page).fill('2')
  await expect(cell(page, 'Even')).toBeDisabled()
  await expect(page.locator('.ff-sic-bo__notice')).toContainText('Insufficient balance')
  await panelButton(page, 'Slip').click()
  await expect(page.locator('.ff-sic-bo__slip > li')).toHaveCount(19)
  await fitsPanel(page)
  expect(await page.locator('.ff-sic-bo__panel-body').evaluate(node => node.scrollHeight > node.clientHeight)).toBe(true)
  await page.getByRole('button', { name: 'Clear', exact: true }).click()
  await expect(page.locator('.ff-sic-bo__slip > li')).toHaveCount(0)
  await page.keyboard.press('Escape')
  expect(fixture.posts).toHaveLength(0)
  fixture.tableStatus.available = false
  await page.reload()
  await expect(page.getByRole('alert')).toContainText('Table unavailable')
  await expect(stake(page)).toBeDisabled()
  await expect(primary(page)).toBeDisabled()
  for (const button of await page.locator('.ff-sic-bo__cell').all()) await expect(button).toBeDisabled()
  expect(fixture.posts).toHaveLength(0)
  await capture(page, info, 'count-funds-unavailable')
})

test('Sic Bo displays server triple wins and losses, and never presents a malformed response as settled', async ({ page }, info) => {
  const fixture = await mockSicBo(page)
  fixture.outcome = 'triple'
  await ready(page)
  for (const label of ['Small', 'Odd', 'Any Triple', 'Triple 3', 'Single 3', 'Double 3']) await add(page, label)
  await primary(page).click()
  await expect(primary(page)).toHaveText('Repeat bets')
  await expect(page.getByRole('img', { name: 'Dice 3, 3, 3', exact: true })).toBeVisible()
  await expect(page.locator('.ff-sic-bo__roll-total')).toHaveText('9Triple')
  await expect(cell(page, 'Small')).toHaveClass(/is-loss/)
  await expect(cell(page, 'Odd')).toHaveClass(/is-loss/)
  await expect(cell(page, 'Any Triple')).toHaveClass(/is-win/)
  await expect(cell(page, 'Single 3')).toHaveClass(/is-win/)
  await expect(page.locator('.ff-sic-bo__return')).toContainText('Return R254.50')
  await capture(page, info, 'triple-mixed-wins-losses')
  await page.getByRole('button', { name: 'New round', exact: true }).click()
  await add(page, 'Small')
  fixture.failNext = 'invalid-response'
  await primary(page).click()
  await expect(primary(page)).toHaveText('Retry roll')
  await expect(page.getByRole('alert')).toContainText('not confirmed')
  await expect(page.locator('.ff-sic-bo__return')).toBeEmpty()
  await expect(page.getByRole('button', { name: 'New round', exact: true })).toHaveCount(0)
  await panelButton(page, 'History').click()
  await expect(page.locator('.ff-sic-bo__history > li')).toHaveCount(1)
  expect(fixture.posts).toHaveLength(2)
  await capture(page, info, 'invalid-response-not-settled')
})

test('Sic Bo keyboard and touch actions retain focus and exact stake intent', async ({ page, browser }, info) => {
  const fixture = await mockSicBo(page)
  await ready(page)
  await stake(page).focus()
  await page.keyboard.press('Control+A')
  await page.keyboard.type('5')
  await cell(page, 'Small').focus()
  await page.keyboard.press('Space')
  await expect(cell(page, 'Small').locator('.ff-sic-bo__marker')).toHaveAttribute('aria-label', 'R5.00 staked')
  await primary(page).focus()
  await page.keyboard.press('Enter')
  await expect(primary(page)).toHaveText('Repeat bets')
  await primary(page).focus(); await page.keyboard.press('Space')
  await expect(primary(page)).toHaveText('Roll dice')
  await expect(primary(page)).toBeFocused()
  expect(fixture.posts).toHaveLength(1)
  await capture(page, info, 'keyboard-repeat-focus')
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, reducedMotion: 'reduce' })
  const phone = await context.newPage()
  try {
    const touchFixture = await mockSicBo(phone)
    await ready(phone)
    await phone.getByRole('button', { name: 'Set stake R5.00', exact: true }).tap()
    await cell(phone, 'Combination 2 + 3').tap()
    await expect(stake(phone)).toHaveValue('5')
    await primary(phone).tap()
    await expect(primary(phone)).toHaveText('Repeat bets')
    expect(touchFixture.posts[0].bets).toEqual([{ ...selections.combination, stake: 5 }])
    expect(touchFixture.posts).toHaveLength(1)
    await fits(phone)
    await capture(phone, info, 'touch-combination-result')
  } finally { await context.close() }
})

test('Sic Bo reveals dice before return, history and New round, retains recovery until completion, and reduces motion immediately', async ({ page }, info) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await page.clock.install()
  const fixture = await mockSicBo(page)
  await ready(page)
  await page.clock.pauseAt(new Date(Date.now() + 1000))
  await add(page, 'Small')
  await primary(page).click()
  await expect(page.locator('.ff-sic-bo')).toHaveAttribute('aria-busy', 'true')
  await expect(page.getByRole('img', { name: 'Dice revealing', exact: true })).toBeVisible()
  await expect(page.locator('.ff-sic-bo__dice .ff-sic-bo__die b')).toHaveCount(3)
  await expect(page.locator('.ff-sic-bo__return')).toBeEmpty()
  expect(await pendingRecord(page, 'account')).not.toBeNull()
  await capture(page, info, 'motion-requested')
  await page.clock.runFor(350)
  await expect(page.locator('.ff-sic-bo__dice .ff-sic-bo__die b')).toHaveCount(1)
  await expect(page.locator('.ff-sic-bo__return')).toBeEmpty()
  await expect(page.getByRole('button', { name: 'New round', exact: true })).toHaveCount(0)
  await panelButton(page, 'History').click()
  await expect(page.locator('.ff-sic-bo__history > li')).toHaveCount(0)
  await page.keyboard.press('Escape')
  await page.clock.runFor(150)
  await expect(page.locator('.ff-sic-bo')).toHaveAttribute('aria-busy', 'true')
  expect(await pendingRecord(page, 'account')).not.toBeNull()
  await capture(page, info, 'motion-last-die-pending')
  await page.clock.runFor(300)
  await expect(primary(page)).toHaveText('Repeat bets')
  await expect(page.locator('.ff-sic-bo')).toHaveAttribute('aria-busy', 'false')
  await expect(page.getByRole('img', { name: 'Dice 2, 2, 3', exact: true })).toBeVisible()
  await expect(page.locator('.ff-sic-bo__return')).toContainText('Return R2.00')
  expect(await pendingRecord(page, 'account')).toBeNull()
  await capture(page, info, 'motion-settled')
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.getByRole('button', { name: 'New round', exact: true }).click()
  await add(page, 'Small')
  await primary(page).click()
  await expect(primary(page)).toHaveText('Repeat bets')
  await expect(page.locator('.ff-sic-bo')).toHaveAttribute('aria-busy', 'false')
  await expect(page.locator('.ff-sic-bo__dice .is-rolling')).toHaveCount(0)
  expect(fixture.posts).toHaveLength(2)
  await capture(page, info, 'reduced-motion-immediate')
})

test('Sic Bo paid ambiguous roll reads first and reuses the exact key only after a verified 404', async ({ page }, info) => {
  const fixture = await mockSicBo(page)
  await ready(page)
  await add(page, 'Small', '5')
  fixture.failNext = 'lost-response'
  await primary(page).click()
  await expect(primary(page)).toHaveText('Retry roll')
  await expect(stake(page)).toBeDisabled()
  await expect(cell(page, 'Big')).toBeDisabled()
  expect(fixture.posts).toHaveLength(1)
  expect(fixture.reads).toHaveLength(0)
  await primary(page).click()
  await expect(primary(page)).toHaveText('Repeat bets')
  expect(fixture.posts).toHaveLength(1)
  expect(fixture.reads).toEqual([fixture.posts[0].roundId])
  await capture(page, info, 'paid-accepted-retry-read-only')
  await page.getByRole('button', { name: 'New round', exact: true }).click()
  await add(page, 'Big', '2')
  fixture.failNext = 'server-error'
  await primary(page).click()
  await expect(primary(page)).toHaveText('Retry roll')
  const uncertain = fixture.posts[1]
  expect(await pendingRecord(page, 'account')).toMatchObject({ idempotencyKey: uncertain.key, bets: [{ ...selections.big, stake: 2 }] })
  await primary(page).click()
  await expect(primary(page)).toHaveText('Repeat bets')
  expect(fixture.posts).toHaveLength(3)
  expect(fixture.posts[2]).toEqual(uncertain)
  expect(fixture.posts[2].key).not.toBe(fixture.posts[0].key)
  expect(fixture.reads.at(-1)).toBe(uncertain.roundId)
  await capture(page, info, 'paid-404-same-key-replay')
})

test('Sic Bo reload restores the bound round using GET only and balance callbacks never duplicate mutations or history', async ({ page }, info) => {
  const fixture = await mockSicBo(page)
  await ready(page)
  await add(page, 'Small', '5')
  fixture.failNext = 'lost-response'
  await primary(page).click()
  await expect(primary(page)).toHaveText('Retry roll')
  const accountReadsBefore = fixture.accountReads
  const pending = await pendingRecord(page, 'account')
  expect(pending).toMatchObject({ scope: player + ':account', roundId: fixture.posts[0].roundId, bets: [{ ...selections.small, stake: 5 }] })
  await page.reload()
  await expect(primary(page)).toHaveText('Repeat bets')
  await expect.poll(() => fixture.accountReads).toBeGreaterThan(accountReadsBefore)
  await expect(page.locator('.ff-sic-bo__summary')).toContainText('Balance R1,005.00')
  expect(fixture.posts).toHaveLength(1)
  expect(fixture.reads).toEqual([fixture.posts[0].roundId])
  expect(await pendingRecord(page, 'account')).toBeNull()
  await panelButton(page, 'History').click()
  await expect(page.locator('.ff-sic-bo__history > li')).toHaveCount(1)
  await capture(page, info, 'reload-get-only-restored')
  await page.keyboard.press('Escape')
  await page.reload()
  await expect(stake(page)).toBeEnabled()
  await panelButton(page, 'History').click()
  await expect(page.locator('.ff-sic-bo__history > li')).toHaveCount(1)
  expect(fixture.posts).toHaveLength(1)
  expect(fixture.reads).toHaveLength(1)
})

test('Sic Bo failed restoration locks edits, retries GET once, and a forged saved ID never reaches the round API', async ({ page }, info) => {
  await page.setViewportSize({ width: 320, height: 568 })
  const fixture = await mockSicBo(page)
  await ready(page, 'practice')
  await add(page, 'Small')
  fixture.failNext = 'lost-response'
  await primary(page).click()
  await expect(page.getByRole('button', { name: 'Retry restoration', exact: true })).toBeVisible()
  expect(fixture.posts[0].practice).toBe(true)
  expect(fixture.posts[0].roundId).toBe(roundId(player, fixture.posts[0].key, 'practice'))
  fixture.failReads = true
  await page.reload()
  await expect(page.getByRole('alert')).toContainText('could not be restored')
  await expect(panelButton(page, 'Slip')).toHaveAccessibleName('Slip 1')
  await expect(cell(page, 'Small').locator('.ff-sic-bo__marker')).toHaveAttribute('aria-label', 'R1.00 staked')
  await expect(page.locator('.ff-sic-bo__summary')).toContainText('Bet R1.00')
  await expect(stake(page)).toBeDisabled()
  await expect(primary(page)).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled()
  await expect(cell(page, 'Small')).toBeDisabled()
  await fits(page); await errorFits(page)
  await capture(page, info, 'failed-read-lock')
  fixture.failReads = false; fixture.readDelay = 150
  await page.getByRole('button', { name: 'Retry restoration', exact: true }).evaluate(node => { (node as HTMLButtonElement).click(); (node as HTMLButtonElement).click() })
  await expect(primary(page)).toHaveText('Repeat bets')
  expect(fixture.reads).toHaveLength(2)
  expect(fixture.posts).toHaveLength(1)
  await page.evaluate(record => sessionStorage.setItem(record.storageKey, JSON.stringify(record.value)), {
    storageKey: pendingKey(player, 'practice'),
    value: { scope: player + ':practice', idempotencyKey: 'sic-bo-forged-saved-key', roundId: 'f'.repeat(64), bets: [selections.small] },
  })
  await page.reload()
  await expect(page.getByRole('alert')).toContainText('could not be restored')
  await expect(primary(page)).toBeDisabled()
  expect(fixture.reads).toHaveLength(2)
  expect(fixture.posts).toHaveLength(1)
  await page.setViewportSize({ width: 500, height: 320 })
  await fits(page); await errorFits(page)
  await capture(page, info, 'forged-saved-id-locked')
})

test('Sic Bo account and practice scopes never disclose or replay another account or mode history', async ({ page }, info) => {
  const fixture = await mockSicBo(page)
  const key = 'sic-bo-other-mode-pending'
  const id = roundId(player, key, 'account')
  const prior = settledFixture([selections.small], id, 1000, 'mixed')
  await page.addInitScript(records => {
    sessionStorage.setItem(records.pendingKey, JSON.stringify(records.pending))
    localStorage.setItem(records.historyKey, JSON.stringify(records.history))
  }, { pendingKey: pendingKey(player, 'account'), historyKey: historyKey(player, 'account'),
    pending: { scope: player + ':account', idempotencyKey: key, roundId: id, bets: [selections.small] },
    history: { scope: player + ':account', rounds: [prior] } })
  await ready(page, 'practice')
  await panelButton(page, 'History').click()
  await expect(page.locator('.ff-sic-bo__history > li')).toHaveCount(0)
  await expect(page.getByRole('dialog')).toContainText('No settled rolls yet')
  expect(fixture.reads).toHaveLength(0)
  expect(fixture.posts).toHaveLength(0)
  await page.keyboard.press('Escape')
  fixture.accountId = 'sic-bo-different-owner'
  await page.goto('/games/sic-bo')
  await expect(stake(page)).toBeEnabled()
  await panelButton(page, 'History').click()
  await expect(page.locator('.ff-sic-bo__history > li')).toHaveCount(0)
  expect(fixture.reads).toHaveLength(0)
  expect(fixture.posts).toHaveLength(0)
  await capture(page, info, 'account-mode-history-isolation')
})

test('Sic Bo definitive fresh 400 and 409 rejections refresh balance and permit editing with a new key', async ({ page }, info) => {
  const fixture = await mockSicBo(page, { balance: 10 })
  await ready(page)
  for (const rejection of [
    { status: 409, code: 'insufficient-slot-credits', message: 'Insufficient balance.', balance: 5 },
    { status: 400, code: 'sic-bo-invalid-request', message: 'Invalid request.', balance: 3 },
  ]) {
    if (await page.getByRole('button', { name: 'New round', exact: true }).count()) await page.getByRole('button', { name: 'New round', exact: true }).click()
    await add(page, 'Small')
    fixture.rejection = rejection
    const statusReadsBefore = fixture.statusReads
    await primary(page).click()
    await expect(page.getByRole('alert')).toContainText('Edit your slip')
    await expect(stake(page)).toBeEnabled()
    await expect.poll(() => fixture.statusReads).toBeGreaterThan(statusReadsBefore)
    expect(await pendingRecord(page, 'account')).toBeNull()
    const rejected = fixture.posts.at(-1)!
    await page.getByRole('button', { name: 'Undo', exact: true }).click()
    await add(page, 'Big', '2')
    await primary(page).click()
    await expect(primary(page)).toHaveText('Repeat bets')
    expect(fixture.posts.at(-1)!.key).not.toBe(rejected.key)
    expect(fixture.posts.at(-1)!.bets).toEqual([{ ...selections.big, stake: 2 }])
  }
  expect(fixture.posts).toHaveLength(4)
  expect(fixture.reads).toHaveLength(0)
  await capture(page, info, 'definitive-rejection-edited-new-key')
})

type Mode = 'account' | 'practice'
type Outcome = 'mixed' | 'triple'
type MutableStatus = { -readonly [Key in keyof SicBoStatus]: SicBoStatus[Key] }
type Post = { key: string; bets: SicBoBetRequest[]; roundId: string; practice: boolean }
type Rejection = { status: number; code: string; message: string; balance: number }
type Fixture = {
  accountId: string; balance: number; tableStatus: MutableStatus; posts: Post[]; reads: string[]; otherWrites: string[];
  accountReads: number; statusReads: number; rounds: Map<string, SicBoRound>; outcome: Outcome;
  failNext: 'lost-response' | 'server-error' | 'invalid-response' | null; failReads: boolean; readDelay: number; rejection: Rejection | null;
}

async function mockSicBo(page: Page, options: { balance?: number } = {}): Promise<Fixture> {
  const fixture: Fixture = {
    accountId: player, balance: options.balance ?? 1000,
    tableStatus: { available: true, minimumStake: 1, maximumStakePerBet: 100, stakeIncrement: 1, maximumBetsPerRound: 20, balance: options.balance ?? 1000, mode: 'three-dice-sic-bo' },
    posts: [], reads: [], otherWrites: [], accountReads: 0, statusReads: 0, rounds: new Map(), outcome: 'mixed', failNext: null, failReads: false, readDelay: 0, rejection: null,
  }
  await page.route('**/api/**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    const practice = request.headers()['x-fortuneforge-practice'] === 'true'
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (path === '/api/accounts/me') {
      fixture.accountReads++
      return json({ userId: fixture.accountId, playerName: 'Sic Bo UX Player', email: 'sic-bo-ux@example.test', createdAtUtc: '2026-01-01T00:00:00Z', role: 'player',
        balances: { slotsCredits: fixture.balance, freeGames: 10 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } })
    }
    if (path === '/api/games/sic-bo/status' && request.method() === 'GET') {
      fixture.statusReads++
      return json({ ...fixture.tableStatus, balance: fixture.balance, mode: practice ? 'practice-three-dice-sic-bo' : 'three-dice-sic-bo' })
    }
    if (path === '/api/games/sic-bo/rounds' && request.method() === 'POST') {
      const body = request.postDataJSON() as { bets: SicBoBetRequest[] }
      const key = request.headers()['idempotency-key'] ?? ''
      const id = roundId(fixture.accountId, key, practice ? 'practice' : 'account')
      fixture.posts.push({ key, bets: body.bets, roundId: id, practice })
      if (fixture.rejection) {
        const rejected = fixture.rejection; fixture.rejection = null; fixture.balance = rejected.balance
        return json({ code: rejected.code, message: rejected.message }, rejected.status)
      }
      const failure = fixture.failNext; fixture.failNext = null
      if (failure === 'server-error') return json({ title: 'Server error', status: 500 }, 500)
      let round = fixture.rounds.get(id)
      if (!round) {
        round = settledFixture(body.bets, id, fixture.balance, fixture.outcome)
        fixture.rounds.set(id, round); fixture.balance = round.balance
      }
      if (failure === 'lost-response') return route.abort('failed')
      if (failure === 'invalid-response') return json({ ...round, total: round.total + 1 }, 201)
      return json(round, 201)
    }
    if (path.startsWith('/api/games/sic-bo/rounds/') && request.method() === 'GET') {
      const id = path.split('/').at(-1)!
      fixture.reads.push(id)
      if (fixture.readDelay) await new Promise(done => setTimeout(done, fixture.readDelay))
      if (fixture.failReads) return json({ code: 'sic-bo-read-unavailable', message: 'Result service unavailable.' }, 503)
      const round = fixture.rounds.get(id)
      return round ? json(round) : json({ code: 'sic-bo-round-not-found', message: 'Round not found.' }, 404)
    }
    if (request.method() !== 'GET') fixture.otherWrites.push(request.method() + ' ' + path)
    return json({ code: 'not-found', message: 'No fixture for this endpoint.' }, 404)
  })
  return fixture
}

function bet(kind: SicBoBetKind, selection: Partial<SicBoBetRequest> = {}): SicBoBetRequest {
  return { kind, stake: 1, face: null, total: null, firstFace: null, secondFace: null, ...selection }
}
function roundId(owner: string, key: string, mode: Mode) {
  return createHash('sha256').update(mode === 'practice' ? `practice\nsic-bo\n${owner}\n${key}` : `${owner}\n${key}`).digest('hex')
}
function pendingKey(owner: string, mode: Mode) { return 'fortuneforge:sic-bo:pending:' + encodeURIComponent(owner + ':' + mode) }
function historyKey(owner: string, mode: Mode) { return 'fortuneforge:sic-bo:history:' + encodeURIComponent(owner + ':' + mode) }
function pendingRecord(page: Page, mode: Mode) { return page.evaluate(key => JSON.parse(sessionStorage.getItem(key) ?? 'null') as unknown, pendingKey(player, mode)) }

/** Explicit public-contract examples, grounded in the host paytable; no product-side settlement is invoked. */
function settledFixture(bets: readonly SicBoBetRequest[], id: string, previousBalance: number, outcome: Outcome): SicBoRound {
  const examples: Record<Outcome, { dice: SicBoRound['dice']; winning: Record<string, number> }> = {
    mixed: { dice: [2, 2, 3], winning: { small: 1, odd: 1, 'single-number:2': 2, 'single-number:3': 1, 'specific-double:2': 11.5, 'total:7': 12, 'two-number-combination:2:3': 6 } },
    triple: { dice: [3, 3, 3], winning: { 'any-triple': 32, 'single-number:3': 12, 'specific-double:3': 11.5, 'specific-triple:3': 195, 'total:9': 7 } },
  }
  const example = examples[outcome]
  const settlements = bets.map((request, betIndex) => {
    const selection = [request.kind, request.face ?? request.total ?? request.firstFace, request.secondFace].filter(value => value !== null).join(':')
    const profitOdds = example.winning[selection] ?? 0
    const won = profitOdds > 0
    const profit = won ? request.stake * profitOdds : -request.stake
    return { ...request, betIndex, won, profitOdds, profit, totalReturn: won ? request.stake + profit : 0 }
  })
  const totalStaked = settlements.reduce((sum, item) => sum + item.stake, 0)
  const totalReturn = settlements.reduce((sum, item) => sum + item.totalReturn, 0)
  const profit = totalReturn - totalStaked
  return { roundId: id, phase: 'settled', dice: example.dice, total: outcome === 'triple' ? 9 : 7, isTriple: outcome === 'triple', totalStaked, totalReturn, profit, balance: previousBalance + profit, settlements }
}

function primary(page: Page) { return page.locator('.ff-sic-bo__primary') }
function stake(page: Page) { return page.getByRole('textbox', { name: 'Stake', exact: true }) }
function cell(page: Page, label: string) { return page.getByRole('button', { name: 'Add ' + label + ' bet', exact: true }) }
function panelButton(page: Page, panel: 'Slip' | 'Rules' | 'History') { return page.getByRole('button', { name: panel === 'Slip' ? /^Slip \d+$/ : panel, exact: panel !== 'Slip' }) }
async function ready(page: Page, mode: Mode = 'account') {
  await page.goto('http://127.0.0.1:4187/games/sic-bo' + (mode === 'practice' ? '?mode=practice' : ''))
  await expect(stake(page)).toBeEnabled()
  await expect(primary(page)).toHaveText('Roll dice')
  await expect(page.locator('.ff-sic-bo__cell')).toHaveCount(52)
}
async function add(page: Page, label: string, value?: string) {
  if (value !== undefined) await stake(page).fill(value)
  await cell(page, label).click()
}
async function contained(locator: Locator, width: number, height: number) {
  await expect(locator).toBeVisible()
  const box = await locator.boundingBox()
  expect(box).not.toBeNull()
  expect(box!.x).toBeGreaterThanOrEqual(-1); expect(box!.y).toBeGreaterThanOrEqual(-1)
  expect(box!.width).toBeGreaterThan(0); expect(box!.height).toBeGreaterThan(0)
  expect(box!.x + box!.width).toBeLessThanOrEqual(width + 1)
  expect(box!.y + box!.height).toBeLessThanOrEqual(height + 1)
}
async function fits(page: Page) {
  const viewport = page.viewportSize()!
  const dimensions = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight, innerWidth, innerHeight }))
  expect(dimensions.width).toBeLessThanOrEqual(dimensions.innerWidth + 1)
  expect(dimensions.height).toBeLessThanOrEqual(dimensions.innerHeight + 1)
  await contained(page.locator('.ff-sic-bo__layout'), viewport.width, viewport.height)
  await contained(page.locator('.ff-sic-bo__controls'), viewport.width, viewport.height)
  await contained(primary(page), viewport.width, viewport.height)
  await contained(stake(page), viewport.width, viewport.height)
  const controls = (await page.locator('.ff-sic-bo__controls').boundingBox())!
  const board = (await page.locator('.ff-sic-bo__board').boundingBox())!
  const marketBoxes = await page.locator('.ff-sic-bo__cell').evaluateAll(nodes => nodes.map(node => {
    const box = node.getBoundingClientRect()
    const center = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
    return { x: box.x, y: box.y, width: box.width, height: box.height, hit: center?.closest('.ff-sic-bo__cell') === node }
  }))
  expect(marketBoxes).toHaveLength(52)
  for (const box of marketBoxes) {
    expect(box.x).toBeGreaterThanOrEqual(0); expect(box.y).toBeGreaterThanOrEqual(0)
    expect(box.width).toBeGreaterThan(0); expect(box.height).toBeGreaterThan(0)
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.width + 1)
    expect(box.y + box.height).toBeLessThanOrEqual(controls.y + 1)
    expect(box.x).toBeGreaterThanOrEqual(board.x - 1)
    expect(box.y).toBeGreaterThanOrEqual(board.y - 1)
    expect(box.x + box.width).toBeLessThanOrEqual(board.x + board.width + 1)
    expect(box.y + box.height).toBeLessThanOrEqual(board.y + board.height + 1)
    expect(box.hit).toBe(true)
  }
}
async function fitsPanel(page: Page) {
  const viewport = page.viewportSize()!
  await contained(page.getByRole('dialog'), viewport.width, viewport.height)
  await contained(page.getByRole('button', { name: 'Close table details' }), viewport.width, viewport.height)
  expect(await page.locator('.ff-sic-bo__panel-body').evaluate(node => node.clientHeight)).toBeGreaterThan(0)
  expect(await page.locator('.ff-sic-bo__panel-body').evaluate(node => node.scrollWidth <= node.clientWidth + 1)).toBe(true)
}
async function errorFits(page: Page) {
  const viewport = page.viewportSize()!
  await contained(page.getByRole('alert'), viewport.width, viewport.height)
  await contained(page.getByRole('button', { name: 'Retry restoration', exact: true }), viewport.width, viewport.height)
}
async function capture(page: Page, info: TestInfo, name: string) {
  const path = info.outputPath(name + '.png')
  await page.screenshot({ path })
  const folder = resolve(info.config.rootDir, '../../../.artifacts/ux-cycle/sic-bo/browser')
  await mkdir(folder, { recursive: true })
  await copyFile(path, resolve(folder, name + '.png'))
  await info.attach(name, { path, contentType: 'image/png' })
}

import { expect, test, type Page, type TestInfo } from '@playwright/test'
import type { KenoRound, KenoRoundRequest, KenoStatus } from '../../../game-packages/games/Keno/client/FortuneForge.Games.Keno.Client/src/contracts'
import type { AccountSummary } from '../../src/features/account/services/accountsApi'

const selectedNumbers = [3, 7, 15]
const drawnNumbers = [3, 7, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38]
const account: AccountSummary = {
  userId: 'keno-ux-player',
  playerName: 'Alex',
  email: 'keno-ux@example.test',
  createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 1_000, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  role: 'Player',
}
const status: KenoStatus = {
  available: true,
  minimumWager: 1,
  maximumWager: 20,
  wagerIncrement: 1,
  balance: 1_000,
  mode: 'credit-keno',
  paytable: [
    { spots: 1, hits: 1, multiplier: 2.5 },
    { spots: 3, hits: 2, multiplier: 2 },
    { spots: 3, hits: 3, multiplier: 27 },
  ],
}

for (const viewport of [
  { name: 'desktop', width: 1280, height: 720 },
  { name: 'phone', width: 390, height: 844 },
  { name: 'landscape', width: 667, height: 375 },
] as const) {
  test(`Keno ${viewport.name} preserves the ticket through draw, settlement, and repeat play`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport)
    const fixture = await mockKeno(page, { holdFirstRound: true })
    await disableDrawAudio(page)
    await openTicket(page)

    const numberGroup = page.getByRole('group', { name: 'Keno number selection' })
    const wager = page.getByRole('combobox', { name: 'Keno wager' })
    const clear = page.getByRole('button', { name: 'Clear selection', exact: true })

    await expect(page.getByRole('button', { name: 'Draw', exact: true })).toBeDisabled()
    await pickTicket(page)
    await wager.selectOption('5')
    await expect(page.getByRole('table', { name: '3-spot Keno payouts' })).toContainText('R135.00')
    await expect(numberGroup.getByRole('button', { pressed: true })).toHaveCount(3)
    await expectPrimaryPlayInViewport(page, viewport)
    await capture(page, testInfo, `${viewport.name}-idle`)
    const idleBoard = await numberGroup.boundingBox()
    const idleControls = await page.getByRole('region', { name: 'Keno draw controls' }).boundingBox()

    await page.getByRole('button', { name: 'Draw', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Starting draw…', exact: true })).toBeDisabled()
    await expect(numberGroup.getByRole('button', { name: 'Number 3', exact: true })).toBeDisabled()
    await expect(numberGroup.getByRole('button', { name: 'Number 16', exact: true })).toBeDisabled()
    await expect(wager).toBeDisabled()
    await expect(clear).toBeDisabled()
    expect(fixture.requests).toHaveLength(1)
    expect(fixture.requests[0].body).toEqual({ ticket: { numbers: selectedNumbers }, wager: 5 })
    expect(fixture.requests[0].key).toMatch(/^keno-/)

    fixture.releaseFirstRound()
    await expect(page.getByText('Drawing live', { exact: true })).toBeVisible()
    await expect(numberGroup.getByRole('button', { name: 'Number 3, hit', exact: true })).toBeDisabled()
    await expect(numberGroup.getByRole('button', { name: 'Number 16', exact: true })).toBeDisabled()
    await expect(wager).toBeDisabled()
    await expect(clear).toBeDisabled()
    await expect(page.getByRole('button', { name: /^Revealing \d+ of 20…$/ })).toBeDisabled()
    await expectPrimaryPlayInViewport(page, viewport)
    expect(await numberGroup.boundingBox()).toEqual(idleBoard)
    expect(await page.getByRole('region', { name: 'Keno draw controls' }).boundingBox()).toEqual(idleControls)
    await expect(page.getByRole('table', { name: '3-spot Keno payouts' })).toBeVisible()
    await capture(page, testInfo, `${viewport.name}-drawing`)

    await expect(page.getByText('Round result', { exact: true })).toBeVisible()
    await expect(page.getByText('R10.00 WIN', { exact: true })).toBeVisible()
    await expect(page.getByText('2 of 3 picks hit.', { exact: true })).toBeVisible()
    await expect(page.getByText('Wager R5.00 · Balance R1,005.00', { exact: true })).toBeVisible()
    await expect(numberGroup.getByRole('button', { name: 'Number 15, missed', exact: true })).toBeDisabled()
    await expect(numberGroup.getByRole('button', { name: 'Number 21, drawn', exact: true })).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Draw again', exact: true })).toBeDisabled()
    await expect(wager).toBeDisabled()
    await expect(clear).toBeDisabled()
    await expectPrimaryPlayInViewport(page, viewport)
    await capture(page, testInfo, `${viewport.name}-result`)

    await expect(page.getByRole('button', { name: 'Draw again', exact: true })).toBeEnabled()
    await expect(wager).toBeEnabled()
    await expect(wager).toHaveValue('5')
    await expect(clear).toBeEnabled()
    for (const number of selectedNumbers) {
      await expect(numberGroup.getByRole('button', { name: `Number ${number}`, exact: true })).toHaveAttribute('aria-pressed', 'true')
    }
    await expect(numberGroup.getByRole('button', { pressed: true })).toHaveCount(3)
    await expect(numberGroup.getByRole('button', { name: /, (hit|missed|drawn)$/ })).toHaveCount(0)

    await page.getByRole('button', { name: 'Draw again', exact: true }).click()
    await expect(page.getByText('Drawing live', { exact: true })).toBeVisible()
    expect(fixture.requests).toHaveLength(2)
    expect(fixture.requests[1].body).toEqual(fixture.requests[0].body)
    expect(fixture.requests[1].key).not.toBe(fixture.requests[0].key)
    await expect(page.getByRole('button', { name: 'Draw again', exact: true })).toBeEnabled()
    await expect(page.getByText('Wager R5.00 · Balance R1,010.00', { exact: true })).toBeVisible()

    await clear.click()
    await expect(numberGroup.getByRole('button', { pressed: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Draw', exact: true })).toBeDisabled()
    expect(fixture.requests).toHaveLength(2)
    const first = numberGroup.getByRole('button', { name: 'Number 1', exact: true })
    await first.focus()
    await page.keyboard.press('ArrowDown')
    await expect(numberGroup.getByRole('button', { name: `Number ${viewport.width <= 540 ? 9 : 11}`, exact: true })).toBeFocused()
    await page.keyboard.press('End')
    await expect(numberGroup.getByRole('button', { name: 'Number 80', exact: true })).toBeFocused()
    await page.keyboard.press('Home')
    await expect(first).toBeFocused()
    await page.keyboard.press('Space')
    await expect(first).toHaveAttribute('aria-pressed', 'true')
  })
}

test('Keno retries a failed request with the original ticket and key, then uses a new key for repeat play', async ({ page }, testInfo) => {
  const fixture = await mockKeno(page, { failFirstRound: true })
  await disableDrawAudio(page)
  await openTicket(page)
  await pickTicket(page)
  await page.getByRole('button', { name: 'Draw', exact: true }).click()

  await expect(page.getByRole('alert')).toHaveText('The connection was interrupted. Please try again.')
  await expect(page.getByRole('button', { name: 'Draw', exact: true })).toBeEnabled()
  await expect(page.getByRole('group', { name: 'Keno number selection' }).getByRole('button', { pressed: true })).toHaveCount(3)
  expect(fixture.requests).toHaveLength(1)
  await capture(page, testInfo, 'request-failed')

  await page.getByRole('button', { name: 'Draw', exact: true }).click()
  await expect(page.getByText('Drawing live', { exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
  expect(fixture.requests).toHaveLength(2)
  expect(fixture.requests[1]).toEqual(fixture.requests[0])
  await expect(page.getByRole('button', { name: 'Draw again', exact: true })).toBeEnabled()
  await expect(page.getByText('Wager R1.00 · Balance R1,001.00', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'Draw again', exact: true }).click()
  await expect(page.getByText('Drawing live', { exact: true })).toBeVisible()
  expect(fixture.requests).toHaveLength(3)
  expect(fixture.requests[2].body).toEqual(fixture.requests[0].body)
  expect(fixture.requests[2].key).not.toBe(fixture.requests[0].key)
})

for (const viewport of [
  { name: 'desktop', width: 1280, height: 720 },
  { name: 'phone', width: 390, height: 844 },
  { name: 'compact-phone', width: 320, height: 568 },
  { name: 'landscape', width: 667, height: 375 },
]) {
  for (const failure of ['invalid', 'network'] as const) {
    test(`Keno ${viewport.name} fits ${failure} initial connection failure and reconnects`, async ({ page }, testInfo) => {
      await page.setViewportSize(viewport)
      const fixture = await mockKeno(page, { statusFailure: failure })
      await disableDrawAudio(page)
      await page.goto('/games/keno')
      const panel = page.getByRole('region', { name: 'Keno connection' })
      await expect(panel).toBeVisible()
      await expect(panel.getByRole('alert')).toBeVisible()
      await expect(page.getByRole('group', { name: 'Keno number selection' })).toHaveCount(0)
      await expect(page.getByRole('combobox', { name: 'Keno wager' })).toHaveCount(0)
      await expect(page.getByRole('region', { name: 'Keno prize table' })).toHaveCount(0)
      const bounds = await panel.boundingBox()
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height + 1)
      const retry = panel.getByRole('button', { name: 'Retry connection', exact: true })
      expect((await retry.boundingBox())!.height).toBeGreaterThanOrEqual(44)
      expect(await page.evaluate(() => Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))).toBeLessThanOrEqual(viewport.height + 1)
      await capture(page, testInfo, `${viewport.name}-${failure}-status`)
      fixture.releaseStatus()
      await retry.click()
      await expect(page.getByRole('combobox', { name: 'Keno wager' })).toBeEnabled()
      await expect(page.getByRole('alert')).toHaveCount(0)
      await pickTicket(page)
      await expectPrimaryPlayInViewport(page, viewport)
      await capture(page, testInfo, `${viewport.name}-${failure}-reconnected`)
      await page.getByRole('button', { name: 'Draw', exact: true }).click()
      await expect(page.getByText('Round result', { exact: true })).toBeVisible()
      expect(fixture.requests).toHaveLength(1)
    })
  }
}

test('Keno shows deliberate table unavailability without a fabricated ticket or reconnect action', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mockKeno(page, { statusFailure: 'unavailable' })
  await page.goto('/games/keno')
  await expect(page.getByRole('alert')).toContainText('temporarily unavailable')
  await expect(page.getByRole('button', { name: 'Retry connection' })).toHaveCount(0)
  await expect(page.getByRole('group', { name: 'Keno number selection' })).toHaveCount(0)
  await capture(page, testInfo, 'phone-table-unavailable')
})

test('Keno keeps failed pending restoration in the primary rail and clears the error after original-key recovery', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 667, height: 375 })
  const fixture = await mockKeno(page, { failRestoration: true })
  await disableDrawAudio(page)
  await openTicket(page)
  await pickTicket(page)
  await page.getByRole('button', { name: 'Draw', exact: true }).click()
  await expect(page.getByRole('alert')).toBeVisible()
  await page.reload()
  const retry = page.getByRole('button', { name: 'Retry restoration', exact: true })
  await expect(retry).toBeEnabled()
  await expectPrimaryPlayInViewport(page, { name: 'landscape', width: 667, height: 375 })
  await capture(page, testInfo, 'landscape-pending-restoration-failed')
  fixture.releaseRestoration()
  await retry.click()
  await expect(page.getByText('Round result', { exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
  expect(fixture.requests.every(request => request.key === fixture.requests[0].key)).toBe(true)
  await expect(page.getByText('Wager R1.00 · Balance R1,001.00', { exact: true })).toBeVisible()
  await capture(page, testInfo, 'landscape-pending-restoration-recovered')
})

for (const failRestoration of [false, true]) {
  test(`Keno retains connection retry when pending restoration ${failRestoration ? 'also fails' : 'succeeds'}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 390, height: 844 })
    const fixture = await mockKeno(page, { statusFailure: 'network', failRestoration })
    await disableDrawAudio(page)
    await page.addInitScript(({ userId, numbers }) => {
      sessionStorage.setItem(`fortuneforge:keno:pending:${userId}`, JSON.stringify({ idempotencyKey: 'keno-combined-recovery', numbers, wager: 1 }))
    }, { userId: account.userId, numbers: selectedNumbers })
    await page.goto('/games/keno')
    const reconnect = page.getByRole('button', { name: 'Retry connection', exact: true })
    if (failRestoration) {
      const panel = page.getByRole('region', { name: 'Keno connection' })
      await expect(panel.getByRole('button', { name: 'Retry restoration', exact: true })).toBeEnabled()
      await expect(reconnect).toBeEnabled()
      const bounds = await panel.boundingBox()
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(844)
      await expect(page.getByRole('group', { name: 'Keno number selection' })).toHaveCount(0)
      await capture(page, testInfo, 'phone-combined-recovery-failed')
      fixture.releaseRestoration()
      await panel.getByRole('button', { name: 'Retry restoration', exact: true }).click()
    }
    await expect(page.getByText('Round result', { exact: true })).toBeVisible()
    await expect(page.getByRole('alert')).toContainText('connection was interrupted')
    await expect(reconnect).toBeEnabled()
    await expect(page.getByRole('combobox', { name: 'Keno wager' })).toHaveValue('1')
    await expect(page.getByText('Balance R1,001.00', { exact: true })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Keno prize table' })).toHaveCount(0)
    await expectPrimaryPlayInViewport(page, { name: 'phone', width: 390, height: 844 })
    await capture(page, testInfo, `phone-restored-without-status-${failRestoration}`)
    fixture.releaseStatus()
    await reconnect.click()
    await expect(page.getByRole('combobox', { name: 'Keno wager' })).toBeEnabled()
    await expect(page.getByRole('alert')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Draw again', exact: true })).toBeEnabled()
    await expect(page.getByText('Wager R1.00 · Balance R1,001.00', { exact: true })).toBeVisible()
    expect(fixture.requests.every(request => request.key === 'keno-combined-recovery')).toBe(true)
    expect(await page.evaluate(userId => sessionStorage.getItem(`fortuneforge:keno:pending:${userId}`), account.userId)).toBeNull()
  })
}

async function openTicket(page: Page) {
  await page.goto('/games/keno')
  await expect(page.getByRole('combobox', { name: 'Keno wager' })).toBeEnabled()
  await page.evaluate(async () => { await document.fonts.ready })
}

async function pickTicket(page: Page) {
  // Deliberately choose an unsorted ticket to verify the submitted ticket is canonical.
  for (const number of [15, 3, 7]) {
    await page.getByRole('button', { name: `Number ${number}`, exact: true }).click()
  }
}

async function disableDrawAudio(page: Page) {
  await page.addInitScript(() => {
    // Draw sound is not under test; its decode/start timing must not affect visual checkpoints.
    Object.defineProperty(window, 'AudioContext', { configurable: true, value: undefined })
    Object.defineProperty(window, 'webkitAudioContext', { configurable: true, value: undefined })
  })
}

async function expectPrimaryPlayInViewport(page: Page, viewport: { name: string; width: number; height: number }) {
  const numberGroup = page.getByRole('group', { name: 'Keno number selection' })
  const controls = page.getByRole('region', { name: 'Keno draw controls' })
  for (const surface of [numberGroup, controls]) {
    const bounds = await surface.boundingBox()
    expect(bounds).not.toBeNull()
    expect(bounds!.x).toBeGreaterThanOrEqual(-1)
    expect(bounds!.y).toBeGreaterThanOrEqual(-1)
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width + 1)
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height + 1)
  }
  if (viewport.name === 'desktop') {
    expect(await page.evaluate(() => Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))).toBeLessThanOrEqual(viewport.height + 1)
  }
}

async function capture(page: Page, testInfo: TestInfo, name: string) {
  const path = testInfo.outputPath(`${name}.png`)
  await page.screenshot({ path, animations: 'disabled', caret: 'hide' })
  await testInfo.attach(name, { path, contentType: 'image/png' })
}

async function mockKeno(page: Page, options: { holdFirstRound?: boolean; failFirstRound?: boolean; statusFailure?: 'invalid' | 'network' | 'unavailable'; failRestoration?: boolean } = {}) {
  const requests: { body: KenoRoundRequest; key: string | undefined }[] = []
  let balance = status.balance
  let statusBlocked = Boolean(options.statusFailure)
  let roundsBlocked = Boolean(options.failRestoration)
  const responses = new Map<string, KenoRound>()
  let resolveFirstRound!: () => void
  const firstRoundResponse = new Promise<void>(resolve => { resolveFirstRound = resolve })

  await page.route('**/api/**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path === '/api/accounts/me') {
      await route.fulfill({ json: { ...account, balances: { ...account.balances, slotsCredits: balance } } })
      return
    }
    if (path === '/api/games/keno/status') {
      if (statusBlocked) {
        if (options.statusFailure === 'network') await route.fulfill({ status: 503, json: { code: 'keno-request-failed', message: 'The connection was interrupted. Please try again.' } })
        else await route.fulfill({ json: options.statusFailure === 'invalid' ? { available: true, balance, mode: 'test' } : { ...status, available: false, balance } })
        return
      }
      await route.fulfill({ json: { ...status, balance } })
      return
    }
    if (path === '/api/games/keno/rounds' && request.method() === 'POST') {
      const body = request.postDataJSON() as KenoRoundRequest
      const key = request.headers()['idempotency-key']
      requests.push({ body, key })
      if (key && responses.has(key)) { await route.fulfill({ json: responses.get(key) }); return }
      if (options.holdFirstRound && requests.length === 1) await firstRoundResponse
      if ((options.failFirstRound && requests.length === 1) || roundsBlocked) {
        await route.fulfill({ status: 503, json: { code: 'keno-request-failed', message: 'The connection was interrupted. Please try again.' } })
        return
      }
      const payout = body.wager * 2
      balance += payout - body.wager
      const round: KenoRound = {
        roundId: `keno-ux-${requests.length}`,
        phase: 'completed',
        ticket: body.ticket,
        draw: { numbers: drawnNumbers },
        hitCount: body.ticket.numbers.filter(number => drawnNumbers.includes(number)).length,
        wager: body.wager,
        payout,
        net: payout - body.wager,
        balance,
        outcome: `R${payout.toFixed(2)} return`,
      }
      if (key) responses.set(key, round)
      await route.fulfill({ json: round })
      return
    }
    await route.fulfill({ status: 404, json: { code: 'ux-fixture-missing', message: 'No response configured for this request.' } })
  })
  return { requests, releaseFirstRound: () => resolveFirstRound(), releaseStatus: () => { statusBlocked = false }, releaseRestoration: () => { roundsBlocked = false } }
}

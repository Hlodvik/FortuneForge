import { test, expect, type APIRequestContext } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import type { PharaohSlotState, PharaohSpinResult } from '../src/types'

const fixture = 'http://127.0.0.1:5301'
const account = 'pharaoh-ascent-local-paid-test'
const evidence = resolve(import.meta.dirname, '../../../../../output/artwork/pharaohs-ascent/browser')
const pause = (ms: number) => new Promise(resolvePause => setTimeout(resolvePause, ms))

async function state(api: APIRequestContext): Promise<PharaohSlotState> {
  const response = await api.get(fixture + '/api/slots/cosmic-fortune/state')
  expect(response.ok(), await response.text()).toBe(true)
  return response.json()
}
async function post<T>(api: APIRequestContext, path: string, body: unknown): Promise<T> {
  const operation = randomUUID()
  for (let attempt = 0; attempt < 20; attempt++) {
    const response = await api.post(fixture + path, { data: body,
      headers: { 'Idempotency-Key': operation, 'X-FortuneForge-Expected-Account': account } })
    if (response.status() === 429) { await pause(550); continue }
    expect(response.ok(), await response.text()).toBe(true)
    return response.json()
  }
  throw new Error('Production spin cooldown did not clear.')
}

test('the standalone paid game naturally earns and settles distinct tomb routes on desktop and mobile', async ({ page, request }) => {
  mkdirSync(evidence, { recursive: true })
  const health = await (await request.get(fixture + '/__fixture/health')).json()
  expect(health).toMatchObject({ localOnly: true, productionEngine: true, testFundsOnly: true, account })
  expect(health.projectId).toMatch(/^demo-pharaoh-paid-/)
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  const initial = await state(request)
  let current = initial
  let spun = 0
  let paid = 0
  let reelPayoutCents = 0
  let chargedCents = 0
  let expeditionPayoutCents = 0
  const prizes: number[] = []

  // All tickets below come from natural paid results; no state or outcomes are injected.
  while (current.gameFeatureState.tomb.tickets.length < 3 && spun < 700) {
    const free = current.freeSpinsRemaining > 0
    const result = await post<PharaohSpinResult>(request, '/api/slots/prepared-spins', {
      gameId: 'cosmic-fortune-v1', wagerPoints: free ? current.freeSpinWagerPoints : 12,
      useFreeSpin: free, useSpecialBoost: false,
    })
    expect(result.chargedWagerCents).toBe(free ? 0 : 300)
    spun++
    if (!free) paid++
    chargedCents += result.chargedWagerCents
    reelPayoutCents += result.payout.totalPoints * result.pointValueInCents
    current = result
  }
  expect(current.gameFeatureState.tomb.tickets.length).toBeGreaterThanOrEqual(3)
  while (current.freeSpinsRemaining > 0) {
    const result = await post<PharaohSpinResult>(request, '/api/slots/prepared-spins', {
      gameId: 'cosmic-fortune-v1', wagerPoints: current.freeSpinWagerPoints, useFreeSpin: true, useSpecialBoost: false,
    })
    expect(result.chargedWagerCents).toBe(0)
    spun++
    reelPayoutCents += result.payout.totalPoints * result.pointValueInCents
    current = result
  }
  await page.goto('/slots/pharaohs-ascent', { waitUntil: 'networkidle' })
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
  await page.screenshot({ path: resolve(evidence, 'desktop-cabinet.png'), fullPage: true })
  const uiAction = async (name: string | RegExp) => {
    await pause(550)
    const button = page.getByRole('button', { name, exact: typeof name === 'string' })
    await expect(button).toBeEnabled()
    const reply = page.waitForResponse(response => response.url().endsWith('/api/slots/cosmic-fortune/actions') &&
      response.request().method() === 'POST')
    await button.click()
    const response = await reply
    expect(response.ok(), await response.text()).toBe(true)
    current = await response.json()
    await expect(page.locator('.sealed-tomb__status')).not.toHaveText('Confirming your expedition…')
  }
  await uiAction('Enter the Sealed Tomb')
  const firstRun = current.gameFeatureState.tomb.activeRun!
  expect(firstRun.bankedPoints).toBe(firstRun.fundedWagerPoints * 5)
  for (let room = 0; room < 3; room++) await uiAction('Open Hidden key')
  expect(current.gameFeatureState.tomb.activeRun!.phase).toBe('finale')
  await page.screenshot({ path: resolve(evidence, 'sarcophagus-ready.png'), fullPage: true })
  await page.reload({ waitUntil: 'networkidle' })
  expect((await state(request)).gameFeatureState.tomb.activeRun!.id).toBe(firstRun.id)
  await uiAction('Open the sarcophagus')
  let result = current.gameFeatureState.tomb.lastRun!
  expect(result.reason).toBe('sarcophagus')
  expect(result.payoutPoints).toBeGreaterThanOrEqual(result.fundedWagerPoints * 16)
  expeditionPayoutCents += result.payoutPoints * 25
  prizes.push(result.payoutPoints * .25)
  await page.screenshot({ path: resolve(evidence, 'sarcophagus-paid.png'), fullPage: true })
  await uiAction('Return to the reels')
  await uiAction('Enter the Sealed Tomb')
  for (let room = 0; room < 6; room++) {
    await uiAction(room < 3 ? /^Open .* relic$/ : 'Open Golden hoard')
    if (room === 2) {
      const banked = current.gameFeatureState.tomb.activeRun!.bankedPoints
      await page.reload({ waitUntil: 'networkidle' })
      expect((await state(request)).gameFeatureState.tomb.activeRun!.bankedPoints).toBe(banked)
      await page.screenshot({ path: resolve(evidence, 'relic-route.png'), fullPage: true })
    }
  }
  result = current.gameFeatureState.tomb.lastRun!
  expect(result.reason).toBe('torches')
  expect(result.roomsOpened).toBe(6)
  expect(result.payoutPoints).toBe(result.basePoints + result.roomPoints * result.relicMultiplier)
  expeditionPayoutCents += result.payoutPoints * 25
  prizes.push(result.payoutPoints * .25)
  await uiAction('Return to the reels')
  await page.setViewportSize({ width: 390, height: 844 })
  await page.reload({ waitUntil: 'networkidle' })
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeVisible()
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)
  expect(overflow).toBe(false)
  await page.screenshot({ path: resolve(evidence, 'mobile-cabinet.png'), fullPage: true })
  const final = await state(request)
  const expectedCents = Math.round(initial.slotsCreditsBalance! * 100) - chargedCents + reelPayoutCents + expeditionPayoutCents
  expect(Math.round(final.slotsCreditsBalance! * 100)).toBe(expectedCents)
  const wallet = await (await request.get(fixture + '/__fixture/wallet')).json()
  expect(errors).toEqual([])
  writeFileSync(resolve(evidence, 'paid-flow.json'), JSON.stringify({
    localOnly: true, testFundsOnly: true, projectId: health.projectId, engineVersion: health.engineVersion,
    naturalPaidSpins: paid, earnedSpins: spun - paid, chargedCents, reelPayoutCents, expeditionPayoutCents,
    expeditionPrizesRand: prizes, finalWalletCents: expectedCents, wallet,
    retainedTicketCount: final.gameFeatureState.tomb.tickets.length, mobileHorizontalOverflow: overflow, browserErrors: errors,
  }, null, 2) + '\n')
})

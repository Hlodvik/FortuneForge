import { test, expect, type APIRequestContext } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

const fixture = 'http://127.0.0.1:5299'
const account = 'crooked-road-local-paid-test'
const evidence = resolve(import.meta.dirname, '../../output/artwork/irish-crooked-road-2026-11-22/browser')
const rules = JSON.parse(readFileSync(new URL('../../games/CrookedRoad/src/FortuneForge.Games.CrookedRoad/Data/crooked-road-v1.json', import.meta.url), 'utf8')) as {
  road: { tilePorts: Record<string, number> }
}
type Tile = { kind: string; rotation: number }
type Road = { revision: number; board: (Tile | null)[]; hand: Tile[]; tickets: unknown[];
  activeBonus: { kind: string; step: number; choice: number | null } | null;
  reachableDestinations: string[]; completedJourneys: number }
type State = { gameFeatureState: Road; freeSpinsRemaining: number; freeSpinWagerPoints: number | null;
  slotsCreditsBalance: number }
type Result = State & { isFreeSpin: boolean; chargedWagerCents: number; pointValueInCents: number;
  payout: { totalPoints: number } }

const pause = (ms: number) => new Promise(resolvePause => setTimeout(resolvePause, ms))
const isPaidSpinUrl = (url: string) => /\/api\/slots\/(?:prepared-)?spins$/.test(url)
async function state(api: APIRequestContext): Promise<State> {
  const response = await api.get(fixture + '/api/slots/crooked-road/state', {
    headers: { 'X-FortuneForge-Expected-Account': account },
  })
  expect(response.ok(), await response.text()).toBe(true)
  return response.json() as Promise<State>
}
async function command<T>(api: APIRequestContext, path: string, body: unknown): Promise<T> {
  const operation = randomUUID()
  for (let attempt = 0; attempt < 12; attempt++) {
    const response = await api.post(fixture + path, {
      headers: { 'Idempotency-Key': operation, 'X-FortuneForge-Expected-Account': account },
      data: body,
    })
    if (response.status() === 429) { await pause(550); continue }
    expect(response.ok(), await response.text()).toBe(true)
    return response.json() as Promise<T>
  }
  throw new Error('The unchanged paid protocol remained in cooldown.')
}
async function spin(api: APIRequestContext, records: Result[], free = false): Promise<State> {
  const before = await state(api)
  const result = await command<Result>(api, '/api/slots/spins', {
    gameId: 'crooked-road-v1', wagerPoints: free ? before.freeSpinWagerPoints : 100,
    useFreeSpin: free, useSpecialBoost: false,
  })
  expect(result.isFreeSpin).toBe(free)
  expect(result.chargedWagerCents).toBe(free ? 0 : 100)
  records.push(result)
  return result
}
async function act(api: APIRequestContext, action: Record<string, unknown>): Promise<State> {
  const before = await state(api)
  return command<State>(api, '/api/slots/crooked-road/actions', {
    ...action, expectedRevision: before.gameFeatureState.revision,
  })
}
function rotation(tile: Tile, required: number): number | null {
  for (let turn = 0; turn < 4; turn++) {
    let ports = rules.road.tilePorts[tile.kind]
    for (let i = 0; i < turn; i++) ports = ((ports << 1) & 15) | (ports >> 3)
    if ((ports & required) === required) return turn
  }
  return null
}
async function connect(api: APIRequestContext, destination: string, records: Result[]) {
  const cells = destination === 'fort' ? [[7, 5], [4, 5], [1, 5]]
    : destination === 'well' ? [[7, 5], [4, 12], [3, 10]] : [[7, 5], [4, 6], [5, 10]]
  const reserved = new Set(cells.map(([cell]) => cell))
  const parking = Array.from({ length: 9 }, (_, cell) => cell).filter(cell => !reserved.has(cell))
  for (const [cell, required] of cells) {
    let fitted = false
    for (let attempt = 0; attempt < 100; attempt++) {
      const road = (await state(api)).gameFeatureState
      const placed = road.board[cell]
      if (placed && rotation(placed, required) !== null) {
        await act(api, { action: 'rotate', cellIndex: cell, rotation: rotation(placed, required) })
        fitted = true
        break
      }
      const handIndex = road.hand.findIndex(tile => rotation(tile, required) !== null)
      if (handIndex >= 0) {
        await act(api, { action: 'place', handIndex, cellIndex: cell,
          rotation: rotation(road.hand[handIndex], required) })
        fitted = true
        break
      }
      const useful = parking.find(index => road.board[index] && rotation(road.board[index]!, required) !== null)
      if (useful !== undefined) {
        await act(api, { action: 'place', handIndex: 0, cellIndex: useful, rotation: 0 })
        continue
      }
      let empty = parking.find(index => road.board[index] === null)
      if (empty === undefined) {
        empty = parking[attempt % parking.length]
        await act(api, { action: 'remove', cellIndex: empty })
      }
      if (road.hand.length === 0) await spin(api, records)
      await act(api, { action: 'place', handIndex: 0, cellIndex: empty, rotation: 0 })
    }
    expect(fitted, 'A naturally earned tile must fit the road; no fixture state is injected.').toBe(true)
  }
  expect((await state(api)).gameFeatureState.reachableDestinations).toContain(destination)
}

test('the paid UI settles all three real journeys and remains usable on desktop and mobile', async ({ page, request }) => {
  mkdirSync(evidence, { recursive: true })
  const health = await (await request.get(fixture + '/__fixture/health')).json()
  expect(health.localOnly).toBe(true)
  expect(health.gameId).toBe('crooked-road-v1')
  expect(health.projectId).toMatch(/^crooked-road-browser-/)
  const records: Result[] = []
  const responses: Promise<void>[] = []
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('response', response => {
    if (isPaidSpinUrl(response.url()) && response.ok()) {
      responses.push(response.json().then(result => { records.push(result as Result) }))
    }
  })
  let current = await state(request)
  const initialCents = Math.round(current.slotsCreditsBalance * 100)
  // An interrupted previous local test is resumed through real bonus settlements.
  while (current.gameFeatureState.activeBonus) {
    const bonus = current.gameFeatureState.activeBonus
    if (bonus.choice === null && bonus.kind !== 'fort') await act(request, {
      action: bonus.kind === 'well' ? 'choose-spring' : 'choose-door', choice: 0,
    })
    current = await spin(request, records, true)
  }
  const startingCompleted = current.gameFeatureState.completedJourneys
  await page.goto('/slots/crooked-road', { waitUntil: 'networkidle' })
  await expect(page.locator('.crooked-road-game[data-game-id="crooked-road-v1"]')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'The Crooked Road', exact: true })).toBeAttached()
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
  const firstPaid = page.waitForResponse(reply => isPaidSpinUrl(reply.url()) && reply.ok())
  await page.getByRole('button', { name: 'Spin the reels', exact: true }).click()
  const firstPaidResult = await (await firstPaid).json() as Result & { wagerPoints: number }
  expect(firstPaidResult.isFreeSpin).toBe(false)
  expect(firstPaidResult.chargedWagerCents).toBe(firstPaidResult.wagerPoints * firstPaidResult.pointValueInCents)
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
  const labels = { well: 'Wishing Well', fort: 'Fairy Fort', hill: 'Hollow Hill' }
  for (const destination of ['well', 'fort', 'hill'] as const) {
    for (let i = 0; i < (destination === 'well' ? 19 : 20); i++) await spin(request, records)
    await connect(request, destination, records)
    await page.reload({ waitUntil: 'networkidle' })
    const depart = page.getByRole('button', { name: new RegExp('Set off.*' + labels[destination]) })
    await expect(depart).toBeEnabled()
    await page.screenshot({ path: resolve(evidence, 'road-ready-' + destination + '.png'), fullPage: true })
    const departure = page.waitForResponse(reply => reply.url().endsWith('/api/slots/crooked-road/actions') &&
      reply.request().method() === 'POST', { timeout: 15_000 })
    await depart.click()
    const acknowledgement = await departure
    expect(acknowledgement.ok(), await acknowledgement.text()).toBe(true)
    await expect.poll(async () => (await state(request)).gameFeatureState.activeBonus?.kind,
      { intervals: [500, 1000], timeout: 10_000 }).toBe(destination)
    if (destination === 'well') {
      await page.getByRole('button', { name: 'Middle spring', exact: true }).click()
      await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
      await page.screenshot({ path: resolve(evidence, 'wishing-well.png'), fullPage: true })
      await page.getByRole('button', { name: 'Start autospin', exact: true }).click()
      await expect.poll(async () => (await state(request)).freeSpinsRemaining, { timeout: 45_000 }).toBe(0)
      await expect(page.getByRole('button', { name: 'Start autospin', exact: true })).toBeVisible()
      await Promise.all(responses)
      const paidBeforeWait = records.filter(result => !result.isFreeSpin).length
      await page.waitForTimeout(1500)
      await Promise.all(responses)
      expect(records.filter(result => !result.isFreeSpin)).toHaveLength(paidBeforeWait)
    } else {
      for (let reveal = 0; reveal < 28; reveal++) {
        const before = await state(request)
        if (!before.gameFeatureState.activeBonus) break
        if (destination === 'hill') {
          const door = page.getByRole('button', { name: 'Door ' + (reveal % 3 + 1), exact: true })
          await expect(door).toBeEnabled()
          await door.click()
        }
        const button = page.getByRole('button', { name: 'Spin the reels', exact: true })
        await expect(button).toBeEnabled()
        const response = page.waitForResponse(reply => isPaidSpinUrl(reply.url()) && reply.ok())
        await button.click()
        const settled = await (await response).json() as Result
        expect(settled.isFreeSpin).toBe(true)
        expect(settled.chargedWagerCents).toBe(0)
        if (settled.freeSpinsRemaining > 0 && destination !== 'hill') await expect(button).toBeEnabled()
        if (settled.freeSpinsRemaining > 0 && destination === 'hill') {
          await expect(page.getByRole('button', { name: 'Door 1', exact: true })).toBeEnabled()
        }
        if (reveal === 0) await page.screenshot({ path: resolve(evidence, destination + '-bonus.png'), fullPage: true })
      }
    }
    current = await state(request)
    expect(current.gameFeatureState.activeBonus).toBeNull()
    await expect(page.getByRole('button', { name: 'Return to the road', exact: true })).toBeVisible()
    await page.screenshot({ path: resolve(evidence, destination + '-complete.png'), fullPage: true })
    await page.getByRole('button', { name: 'Return to the road', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
    await expect(page.locator('.slots-page__footer--error')).toHaveCount(0)
  }
  await Promise.all(responses)
  current = await state(request)
  expect(current.gameFeatureState.completedJourneys).toBe(startingCompleted + 3)
  const expected = records.reduce((cents, result) => cents - result.chargedWagerCents +
    result.payout.totalPoints * result.pointValueInCents, initialCents)
  expect(Math.round(current.slotsCreditsBalance * 100)).toBe(expected)
  await page.reload({ waitUntil: 'networkidle' })
  await page.screenshot({ path: resolve(evidence, 'desktop-paid.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.reload({ waitUntil: 'networkidle' })
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeVisible()
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)
  expect(overflow).toBe(false)
  await page.screenshot({ path: resolve(evidence, 'mobile-paid.png'), fullPage: true })
  expect(errors).toEqual([])
  writeFileSync(resolve(evidence, 'paid-flow.json'), JSON.stringify({
    localOnly: health.localOnly, projectId: health.projectId, account, screenSelector: '.crooked-road-game',
    gameId: health.gameId, engineVersion: health.engineVersion,
    initialWalletCents: initialCents, finalWalletCents: Math.round(current.slotsCreditsBalance * 100),
    expectedWalletCents: expected, paidRounds: records.filter(result => !result.isFreeSpin).length,
    bonusRounds: records.filter(result => result.isFreeSpin).length,
    chargedWagerCents: records.reduce((total, result) => total + result.chargedWagerCents, 0),
    payoutCents: records.reduce((total, result) => total + result.payout.totalPoints * result.pointValueInCents, 0),
    completedJourneys: current.gameFeatureState.completedJourneys - startingCompleted,
    browserErrors: errors, mobileHorizontalOverflow: overflow,
  }, null, 2) + '\n')
})


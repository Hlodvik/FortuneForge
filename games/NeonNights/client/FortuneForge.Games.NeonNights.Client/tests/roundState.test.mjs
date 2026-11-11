import assert from 'node:assert/strict'
import { test } from 'node:test'
import { emptyNeonState, getNeonCharge, getNeonRows, getNeonWager, previewRound } from '../dist/roundState.js'
import { createNeonApi, isNeonSpinResult } from '../dist/api.js'

test('Car plays expand the cabinet and charge the locked triggering wager', () => {
  const car = previewRound('6', 150)
  assert.equal(car.freeSpinsRemaining, 7)
  assert.equal(car.freeSpinFeatureMode, 'car-rows')
  assert.equal(getNeonWager(car, 500), 150)
  assert.equal(getNeonCharge(car, 500), 150)
  assert.equal(getNeonRows(car), 7)
  const legacy = { ...car, freeSpinFeatureMode: 'arcade-rows' }
  assert.equal(getNeonCharge(legacy, 500), 150)
  assert.equal(getNeonRows(legacy), 7)
})
test('Disco keeps the chosen wager and its own twelve-spin state', () => {
  const disco = previewRound('POWER', 150)
  assert.equal(disco.neonDiscoSpinsRemaining, 12)
  assert.equal(disco.freeSpinsRemaining, 0)
  assert.equal(getNeonWager(disco, 500), 500)
  assert.equal(getNeonCharge(disco, 500), 500)
  assert.equal(getNeonRows(disco), 4)
})
const result = () => ({ ...emptyNeonState, spinId: 'neon-result', gameId: 'neon-nights-v1', symbolSetId: 'neon-arcade-symbols-v3',
  wagerPoints: 50, pointValueInCents: 1, reels: Array.from({ length: 5 }, () => ['2', '3', '4', '5']),
  isFreeSpin: false, freeSpinsAwarded: 0, slotsCreditsBalance: 10, bananaBonusPoints: 0,
  payout: { totalPoints: 0, paylines: [] }, neonArcade: { featureGamesEnabled: true, bonusSymbolCount: 0, activators: [] } })
test('Neon validates its own identity, active symbols and rectangular row count', () => {
  assert.ok(isNeonSpinResult(result()))
  assert.equal(isNeonSpinResult({ ...result(), gameId: 'another-game' }), false)
  assert.equal(isNeonSpinResult({ ...result(), pointValueInCents: 25 }), false)
  const malformed = result(); malformed.reels[0][0] = 'BOLT'
  assert.equal(isNeonSpinResult(malformed), false)
  const mixedRows = result(); mixedRows.reels[1].push('7')
  assert.equal(isNeonSpinResult(mixedRows), false)
})
test('Neon rejects cascade totals that disagree with the paid result', () => {
  const value = result()
  value.symbolSetId = 'neon-disco-symbols-v1'
  const disco = ['2', '4', '5', '7', 'DISCO_MARTINI', 'DISCO_SEVEN', 'DISCO_BALL']
  value.reels = Array.from({ length: 5 }, (_, reel) => Array.from({ length: 4 }, (_, row) => disco[(reel + row) % disco.length]))
  value.neonDiscoCascade = { initialReels: value.reels, steps: [], totalPoints: 1 }
  assert.equal(isNeonSpinResult(value), false)
  value.neonDiscoCascade.totalPoints = 0
  assert.ok(isNeonSpinResult(value))
})
test('Paid play uses the injected session transport and sends only Neon spin fields', async () => {
  const calls = []
  const api = createNeonApi(async (path, init) => { calls.push([path, init]); return Response.json(result()) })
  await api.spin({ wagerPoints: 50, useFreeSpin: false, state: emptyNeonState }, false)
  assert.equal(calls[0][0], '/api/slots/spins')
  assert.deepEqual(JSON.parse(calls[0][1].body), { gameId: 'neon-nights-v1', wagerPoints: 50, useFreeSpin: false, useSpecialBoost: false })
})
test('Admin triggers accept the empty success response before loading authoritative state', async () => {
  const calls = []
  const expected = previewRound('6', 150)
  const api = createNeonApi(async (path, init) => {
    calls.push([path, init])
    return path.endsWith('/neon-trigger') ? new Response(null, { status: 204 }) : Response.json(expected)
  })
  assert.deepEqual(await api.trigger('car', 150), expected)
  assert.equal(calls[1][0], '/api/slots/state?gameId=neon-nights-v1')
})

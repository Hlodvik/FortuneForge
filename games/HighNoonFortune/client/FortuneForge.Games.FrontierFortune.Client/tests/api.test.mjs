import test from 'node:test'
import assert from 'node:assert/strict'
import {
  FrontierRequestError, parseFrontierState, readFrontier, recoverFrontierFailure, sendFrontier,
} from '../dist/api.js'

const fixture = overrides => ({
  gameId: 'high-noon-fortune-v1', engineVersion: 'frontier-fortune-engine-v2',
  balanceCents: 20_000, revision: 12, lastWinPoints: 0,
  reels: [['J', 'Q', 'K'], ['A', 'HAT', 'SPUR'], ['POUCH', 'BAR', 'WILD'], ['BADGE', 'Q', 'REVOLVER'], ['WANTED', 'K', 'A']],
  wins: [], handRound: null, pendingOperation: null,
  ante: { chips: 3, requiredChips: 12, wagerPointsTotal: 12, fundedWagerPoints: 4, ready: false },
  wagering: { pointValueInCents: 25, minWagerPoints: 4, maxWagerPoints: 400, stepPoints: 4 },
  ...overrides,
})
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

test('paid state parsing accepts all twelve final symbol IDs without mutating its input', () => {
  const state = fixture()
  delete state.wins
  const parsed = parseFrontierState(state)
  assert.equal(parsed.balanceCents, 20_000)
  assert.deepEqual(parsed.wins, [])
  assert.equal('wins' in state, false)
})

test('foreign engines, malformed grids, nonfinite balances and malformed pending actions are rejected', () => {
  for (const change of [
    { gameId: 'other-game' }, { engineVersion: 'old-engine' }, { balanceCents: NaN },
    { reels: [['J']] }, { pendingOperation: { operationId: 'recover', kind: 'spin', wagerPoints: 5 } },
    { pendingOperation: { operationId: 'recover', kind: 'confirm', roundId: 'h', selectedRows: [0,1,2,0,3], expectedRevision: 1 } },
    { ante: { chips: 12, requiredChips: 12, wagerPointsTotal: NaN, fundedWagerPoints: 4, ready: true } },
  ]) assert.throws(() => parseFrontierState(fixture(change)))
})

test('spin transport posts only paid operation ID and exact points wager', async () => {
  let request
  await sendFrontier(async (url, init) => { request = { url, init }; return json(fixture()) }, {
    operationId: 'paid-spin-1', kind: 'spin', wagerPoints: 24, roundId: 'irrelevant', expectedRevision: 8,
  })
  assert.equal(request.url, '/api/slots/frontier-fortune/spins')
  assert.equal(request.init.method, 'POST')
  assert.deepEqual(JSON.parse(request.init.body), { operationId: 'paid-spin-1', wagerPoints: 24 })
})

test('durably declined wager accepts authoritative balance and unlocks the next choice', async () => {
  const command = { operationId: 'declined-spin', kind: 'spin', wagerPoints: 400 }
  let rejection
  try {
    await sendFrontier(async () => json(fixture({ accepted: false, error: 'Insufficient credits.', balanceCents: 25 }), 409), command)
  } catch (cause) { rejection = cause }
  const result = await recoverFrontierFailure(rejection, command, async () => { throw new Error('Must not refetch a complete reply.') })
  assert.equal(result.state.balanceCents, 25)
  assert.equal(result.retryCommand, null)
  assert.equal(result.requiresReload, false)
})

test('stale action refreshes authoritative state and recovers an actual pending command with original terms', async () => {
  const pending = { operationId: 'original-confirm', kind: 'confirm', roundId: 'hand-4', selectedRows: [0,1,null,2,1], expectedRevision: 11 }
  const rejected = new FrontierRequestError('The board changed.', 409)
  const result = await recoverFrontierFailure(rejected, { operationId: 'stale-click', kind: 'deal-in' }, async url => {
    assert.equal(url, '/api/slots/frontier-fortune/state')
    return json(fixture({ pendingOperation: pending }))
  })
  assert.deepEqual(result.retryCommand, pending)
  let posted
  await sendFrontier(async (_url, init) => { posted = JSON.parse(init.body); return json(fixture()) }, result.retryCommand)
  assert.deepEqual(posted, pending)
})

test('transport failure and retry-required 503 retain the identical operation for safe recovery', async () => {
  const command = { operationId: 'uncertain-deal', kind: 'deal', roundId: 'hand-3', held: [true,false,true,false,false], expectedRevision: 3 }
  for (const cause of [new TypeError('Connection lost.'), new FrontierRequestError('retry-required', 503)]) {
    const result = await recoverFrontierFailure(cause, command, async () => { throw new Error('Unexpected request.') })
    assert.equal(result.retryCommand, command)
    assert.equal(result.requiresReload, false)
  }
})

test('definitive rejection with failed state refresh requires reload rather than retrying the rejected operation', async () => {
  const result = await recoverFrontierFailure(new FrontierRequestError('Invalid wager.', 400),
    { operationId: 'invalid-spin', kind: 'spin', wagerPoints: 5 }, async () => { throw new TypeError('Offline') })
  assert.equal(result.retryCommand, null)
  assert.equal(result.requiresReload, true)
  assert.equal(result.state, null)
})

test('GET state never generates or posts a gameplay action', async () => {
  let calls = 0
  await readFrontier(async (url, init) => {
    calls++
    assert.equal(url, '/api/slots/frontier-fortune/state')
    assert.equal(init.method, undefined)
    assert.equal(init.body, undefined)
    return json(fixture())
  })
  assert.equal(calls, 1)
})

test('an omitted hand normalizes to null while the retired target-board engine is refused', () => {
  const state = fixture(); delete state.handRound
  assert.equal(parseFrontierState(state).handRound, null)
  assert.throws(() => parseFrontierState(fixture({ engineVersion: 'frontier-fortune-engine-v1' })), /earlier frontier round/)
})

test('held masks and selected rows retain exact recovery terms with no synthetic card choices', async () => {
  const command = { operationId: 'held-deal', kind: 'deal', expectedRevision: 12, roundId: 'hand-2', held: [true,true,false,false,true] }
  let body
  await sendFrontier(async (url, init) => { assert.equal(url, '/api/slots/frontier-fortune/hand-actions'); body = JSON.parse(init.body); return json(fixture()) }, command)
  assert.deepEqual(body, command)
  assert.throws(() => parseFrontierState(fixture({ pendingOperation: { ...command, held: [true,false] } })))
})

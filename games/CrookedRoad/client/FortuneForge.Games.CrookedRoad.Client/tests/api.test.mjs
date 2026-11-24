import test, { after } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtemp, readFile, writeFile, mkdir, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { pathToFileURL } from 'node:url'
import ts from 'typescript'

// Execute the owned source without requiring a stale dist build or host imports.
const compiled = await mkdtemp(join(tmpdir(), 'crooked-road-api-test-'))
after(() => rm(compiled, { recursive: true, force: true }))
await writeFile(join(compiled, 'package.json'), '{"type":"module"}')
await mkdir(join(compiled, 'assets/contracts'), { recursive: true })
const contract = await readFile(new URL('../src/assets/contracts/crooked-road-v1.json', import.meta.url), 'utf8')
const rules = JSON.parse(contract)
await writeFile(join(compiled, 'assets/contracts/crooked-road-v1.json'), contract)
const preparedUrl = import.meta.resolve('@fortuneforge/slot-client-core/preparedSpin')
for (const name of ['types', 'road', 'api']) {
  const source = (await readFile(new URL(`../src/${name}.ts`, import.meta.url), 'utf8'))
    .replaceAll("'@fortuneforge/slot-client-core/preparedSpin'", JSON.stringify(preparedUrl))
  const output = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText
  await writeFile(join(compiled, `${name}.js`), output)
}
const { createCrookedRoadApi, CrookedRoadRequestError, decodeCrookedRoadState, decodeCrookedRoadSpinResult } = await import(pathToFileURL(join(compiled, 'api.js')))
const account = () => `api-test-${crypto.randomUUID()}`
const road = (revision = 0) => ({
  contractVersion: 1, revision, board: Array(9).fill(null), hand: [], queuedTiles: [],
  reserveTileCounts: { straight: 0, corner: 0, junction: 0 },
  funding: { spins: 0, requiredSpins: rules.fundingSpins, wagerPointsTotal: 0 },
  tickets: [], activeBonus: null, reachableDestinations: [], lastTravel: null, completedJourneys: 0,
})
const state = (revision = 0) => ({ gameFeatureState: road(revision), featureActionRequired: false,
  freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
  slotsCreditsBalance: 100, pendingOperation: null, specialPointsBalance: 0, energyBalance: 0, sealCollections: [], paidMode: null })
function result(wagerPoints = 100, overrides = {}) {
  return { ...state(1), spinId: crypto.randomUUID(), gameId: 'crooked-road-v1',
    reelSetId: 'crooked-road-independent-reels-v1', symbolSetId: 'crooked-road-v1-symbols', paytableId: rules.modelId,
    wagerPoints, pointValueInCents: rules.pointValueInCents, reelStops: [0, 0, 0, 0, 0],
    reels: ['2', '3', '4', '5', '6'].map(symbol => [symbol, symbol, symbol]),
    payout: { totalPoints: 0, paylines: [] }, isFreeSpin: false, chargedWagerCents: wagerPoints,
    spinFeature: null, consecutiveFiveMisses: 0, fiveMatchPityTriggered: false, freeSpinsAwarded: 0,
    collectionFreeSpinsAwarded: 0, specialPointsAwarded: 0, energyAwarded: 0, energyMultiplierApplied: false,
    payoutMultiplier: 1, monkeyPawCount: 0, moneyGrabPoints: 0, bananaBonusPoints: 0,
    specialBoostApplied: false, sealsAwarded: {}, ...overrides }
}
const reply = (body, status = 200, prepared = true) => new Response(JSON.stringify(body), {
  status, headers: { 'Content-Type': 'application/json', ...(prepared ? { 'X-FortuneForge-Prepared-Spin': '1' } : {}) },
})
const request = init => ({ body: JSON.parse(init.body), headers: new Headers(init.headers) })

test('paid transport sends only intent with account and idempotency headers', async () => {
  const id = account(), calls = []
  const api = createCrookedRoadApi(async (path, init) => {
    calls.push([path, init])
    return reply(init?.method === 'POST' ? result(300) : state())
  }, id)
  const spin = await api.spin(300, false)
  assert.equal(spin.chargedWagerCents, 300)
  assert.deepEqual(calls.map(([path]) => path), ['/api/slots/state?gameId=crooked-road-v1', '/api/slots/prepared-spins'])
  const sent = request(calls[1][1])
  assert.equal(sent.headers.get('X-FortuneForge-Expected-Account'), id)
  assert.match(sent.headers.get('Idempotency-Key'), /^[a-f\d-]{36}$/)
  assert.deepEqual(sent.body, { gameId: 'crooked-road-v1', wagerPoints: 300, useFreeSpin: false, useSpecialBoost: false })
  assert.equal(api.pending(), undefined)
})

test('lost paid reply recovers exact operation and terms across remount and state refresh', async () => {
  const id = account(), posts = []
  let lose = true
  const fetch = async (_path, init) => {
    if (init?.method !== 'POST') return reply(state())
    posts.push(request(init))
    if (lose) { lose = false; throw new TypeError('connection lost after settlement') }
    return reply(result(200))
  }
  const first = createCrookedRoadApi(fetch, id)
  await assert.rejects(first.spin(200, false), /connection lost/)
  const operation = first.pending()
  const remounted = createCrookedRoadApi(fetch, id)
  await remounted.getState()
  assert.deepEqual(remounted.pending(), operation)
  await remounted.spin(5000, true)
  assert.deepEqual(posts[1].body, posts[0].body)
  assert.equal(posts[1].headers.get('Idempotency-Key'), posts[0].headers.get('Idempotency-Key'))
  assert.equal(remounted.pending(), undefined)
})

test('malformed success never releases a prepared operation', async () => {
  let valid = false
  const api = createCrookedRoadApi(async (_path, init) => reply(init?.method === 'POST'
    ? valid ? result() : result(100, { gameId: 'foreign-game' }) : state()), account())
  await assert.rejects(api.spin(100, false), error => error instanceof CrookedRoadRequestError && error.code === 'invalid-crooked-road-reply')
  const pending = api.pending()
  assert.ok(pending)
  valid = true
  await api.resumeSpin()
  assert.equal(api.pending(), undefined)
})

test('state bootstrap discovers and replays the server pending operation before a new intent', async () => {
  const pending = { operationId: crypto.randomUUID(), wagerPoints: 400, useFreeSpin: false, useSpecialBoost: false }
  let sent
  const api = createCrookedRoadApi(async (_path, init) => {
    if (init?.method !== 'POST') return reply({ ...state(), pendingOperation: pending })
    sent = request(init)
    return reply(result(400))
  }, account())
  await api.spin(100, false)
  assert.equal(sent.headers.get('Idempotency-Key'), pending.operationId)
  assert.equal(sent.body.wagerPoints, 400)
})

test('an unsupported gateway never receives an unkeyed or alternate spin request', async () => {
  const calls = []
  const api = createCrookedRoadApi(async (path, init) => { calls.push([path, init]); return reply(state(), 200, false) }, account())
  await assert.rejects(api.spin(100, false), error => error.status === 503 && error.code === 'prepared-spin-unavailable')
  assert.equal(calls.length, 1)
  assert.equal(api.pending(), undefined)
  assert.throws(() => createCrookedRoadApi().spin(100, false), error => error.status === 401)
})

test('explicit wallet decline releases the key; unknown conflict preserves it', async () => {
  let decline = false
  const api = createCrookedRoadApi(async (_path, init) => init?.method === 'POST'
    ? reply(decline ? { code: 'insufficient-slot-credits', preparedOperationPending: false, available: 0, required: 1 }
      : { code: 'crooked-road-wallet-state-changed' }, 409) : reply(state()), account())
  await assert.rejects(api.spin(100, false))
  assert.ok(api.pending())
  decline = true
  await assert.rejects(api.resumeSpin(), error => error.available === 0 && error.required === 1)
  assert.equal(api.pending(), undefined)
})

test('a pending-operation conflict adopts the server terms for exact retry', async () => {
  const server = { operationId: crypto.randomUUID(), wagerPoints: 500, useFreeSpin: false, useSpecialBoost: false }
  let count = 0, sent
  const api = createCrookedRoadApi(async (_path, init) => {
    if (init?.method !== 'POST') return reply(state())
    sent = request(init)
    return count++ === 0 ? reply({ code: 'slot-operation-conflict', pendingOperation: server }, 409) : reply(result(500))
  }, account())
  await assert.rejects(api.spin(100, false))
  assert.deepEqual(api.pending(), server)
  await api.resumeSpin()
  assert.equal(sent.headers.get('Idempotency-Key'), server.operationId)
  assert.equal(sent.body.wagerPoints, 500)
})

test('road actions retain their key across a lost reply and return validated full state', async () => {
  const posts = []
  let lose = true
  const api = createCrookedRoadApi(async (_path, init) => {
    posts.push(request(init))
    if (lose) { lose = false; throw new Error('lost action response') }
    return reply(state(1))
  }, account())
  const intent = { expectedRevision: 0, action: 'rotate', cellIndex: 7, rotation: 1 }
  await assert.rejects(api.action(intent), /lost action/)
  await assert.rejects(api.action({ ...intent, cellIndex: 4 }), error => error.code === 'road-action-pending')
  assert.throws(() => api.spin(100, false), error => error.code === 'road-action-pending')
  assert.equal((await api.action(intent)).gameFeatureState.revision, 1)
  assert.equal(posts[0].headers.get('Idempotency-Key'), posts[1].headers.get('Idempotency-Key'))
  assert.deepEqual(posts[0].body, intent)
})

test('owned decoders reject impossible entitlement envelopes and invented payouts', () => {
  assert.ok(decodeCrookedRoadState(state()))
  assert.equal(decodeCrookedRoadState({ ...state(), freeSpinsRemaining: 1 }), null)
  assert.equal(decodeCrookedRoadState({ ...state(), featureActionRequired: true }), null)
  assert.equal(decodeCrookedRoadState({ ...state(), pendingOperation: { operationId: crypto.randomUUID(), wagerPoints: 100, useFreeSpin: false, useSpecialBoost: true } }), null)
  assert.ok(decodeCrookedRoadSpinResult(result()))
  for (const changes of [{ chargedWagerCents: 0 }, { symbolSetId: 'samurai-v1' }, { reels: [['2']] },
    { payout: { totalPoints: 1, paylines: [] } }, { freeSpinsAwarded: 1 }, { spinFeature: {} }]) {
    assert.equal(decodeCrookedRoadSpinResult(result(100, changes)), null)
  }
})

test('payline verification uses canonical wild interpretation, exact positions and summed awards', () => {
  const reels = Array.from({ length: 5 }, () => ['ACE', 'ACE', 'ACE'])
  const fox = rules.symbols.find(symbol => symbol.id === '7')
  const amountPoints = Math.floor(100 * fox.pays[2] * rules.payoutScaleNumerator / (rules.payoutUnits * rules.payoutScaleDenominator))
  const paylines = rules.paylines.map((route, index) => {
    const positions = route.map((row, reel) => ({ reel, row }))
    return { paylineId: index + 1, amountPoints, matches: [{ amountPoints, multiplier: fox.pays[2],
      match: { paylineId: index + 1, symbolId: '7', matchLength: 5, positions, wildPositions: positions } }] }
  })
  const valid = result(100, { reels, payout: { totalPoints: amountPoints * paylines.length, paylines } })
  assert.ok(decodeCrookedRoadSpinResult(valid))
  const forged = structuredClone(valid)
  forged.payout.paylines[0].matches[0].match.wildPositions.pop()
  assert.equal(decodeCrookedRoadSpinResult(forged), null)
})

test('destination events validate terminal doors without trusting hidden decks', () => {
  const active = { id: 'funded-ticket', kind: 'hill', step: 0, totalSteps: 3, awardPoints: 0,
    fundedWagerPoints: 100, choice: null, board: null, held: null, respinsRemaining: 0,
    doors: [0, 1, 2].map(index => ({ index, selected: false, revealed: false, prizePoints: null })) }
  const snapshot = state()
  snapshot.gameFeatureState.activeBonus = active
  snapshot.gameFeatureState.lastTravel = { destination: 'hill', path: [7, 8, 5] }
  Object.assign(snapshot, { freeSpinsRemaining: 3, freeSpinWagerPoints: 100, freeSpinFeatureMode: 'hill', featureActionRequired: true })
  assert.ok(decodeCrookedRoadState(snapshot))
  const terminal = { ...active, step: 3, awardPoints: 600,
    doors: active.doors.map(door => ({ ...door, selected: door.index === 1, revealed: door.index === 1, prizePoints: door.index === 1 ? 200 : null })) }
  const completed = result(100, { reels: Array.from({ length: 5 }, () => ['POWER', 'POWER', 'POWER']),
    gameFeatureState: { ...road(4), lastTravel: snapshot.gameFeatureState.lastTravel, completedJourneys: 1 },
    isFreeSpin: true, chargedWagerCents: 0, payout: { totalPoints: 200, paylines: [] },
    spinFeature: { kind: 'hill', completed: true, bonus: terminal } })
  assert.ok(decodeCrookedRoadSpinResult(completed))
  assert.equal(decodeCrookedRoadSpinResult({ ...completed, spinFeature: { ...completed.spinFeature, completed: false } }), null)
  terminal.doors[0].prizePoints = 400
  assert.equal(decodeCrookedRoadSpinResult(completed), null)
})

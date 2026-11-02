import assert from 'node:assert/strict'

// Sample-server smoke only. Never point this script at an authenticated game,
// production service or remote host: it creates disposable local practice hands.
const origin = new URL(process.argv[2] ?? 'http://127.0.0.1:5185')
assert(['127.0.0.1', 'localhost', '[::1]'].includes(origin.hostname), 'Only a loopback sample server is permitted.')
assert.equal(origin.protocol, 'http:', 'Use the local sample HTTP endpoint.')
assert(!origin.username && !origin.password, 'Credentials are not supported.')
assert.equal(origin.pathname, '/', 'Pass only the sample server origin, not an API path.')
const api = new URL('/api/games/craps/', origin)
const maximumSessions = 100
const maximumRollsPerHand = 100
let requests = 0
let rolls = 0

async function request(path, { method = 'GET', body, status = 200, includeStatus = false } = {}) {
  requests += 1
  const response = await fetch(new URL(path, api), {
    method,
    redirect: 'error',
    headers: body === undefined ? undefined : { 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(8000),
  })
  const value = await response.json()
  const acceptedStatuses = Array.isArray(status) ? status : [status]
  assert(acceptedStatuses.includes(response.status), `${method} ${path}: expected ${acceptedStatuses}, got ${response.status}: ${JSON.stringify(value)}`)
  if (response.status >= 400) {
    assert.equal(typeof value.code, 'string', `${path}: error code is required`)
    assert.equal(typeof value.message, 'string', `${path}: error message is required`)
  }
  return includeStatus ? { status: response.status, value } : value
}

const create = (body, status = 201) => request('rounds', { method: 'POST', body, status })
const read = id => request(`rounds/${id}`)
const roll = async (id, status = 200) => {
  if (status === 200) rolls += 1
  return request(`rounds/${id}/roll`, { method: 'POST', status })
}
const odds = (id, stake, status = 200) => request(`rounds/${id}/odds`, { method: 'POST', body: { stake }, status })
const cents = value => Math.round(value * 100)
const gross = (kind, total, stake) => kind === 'field'
  ? [2, 12].includes(total) ? stake * 3 : [3, 4, 9, 10, 11].includes(total) ? stake * 2 : 0
  : kind === 'any-seven' ? total === 7 ? stake * 5 : 0
    : [2, 3, 12].includes(total) ? stake * 8 : 0

function checkRound(round) {
  assert.equal(typeof round.roundId, 'string')
  assert(['come-out', 'point', 'resolved'].includes(round.phase))
  assert.equal(round.stake, 10)
  assert(Array.isArray(round.rolls))
  round.rolls.forEach((item, index) => {
    assert.equal(item.rollNumber, index + 1)
    assert(Number.isInteger(item.first) && item.first >= 1 && item.first <= 6)
    assert(Number.isInteger(item.second) && item.second >= 1 && item.second <= 6)
    assert.equal(item.total, item.first + item.second)
  })
  const latest = round.rolls.at(-1)
  if (!latest) {
    assert.equal(round.phase, 'come-out')
    assert.equal(round.point, null)
    assert.equal(round.lastOutcome, null)
  } else {
    assert.equal(round.lastOutcome.first, latest.first)
    assert.equal(round.lastOutcome.second, latest.second)
    assert.equal(round.lastOutcome.total, latest.total)
    assert.equal(round.lastOutcome.result, latest.result)
    assert.equal(round.lastOutcome.isTerminal, round.phase === 'resolved')
    if (round.phase === 'point') {
      assert([4, 5, 6, 8, 9, 10].includes(round.point))
      assert.equal(round.lastOutcome.totalReturn, null)
    }
  }
}

function checkSideBets(round, firstTotal) {
  const sides = round.extraBets.filter(bet => bet.kind !== 'odds')
  assert.deepEqual(sides.map(bet => [bet.kind, bet.stake]), [['field', 2], ['any-seven', 3], ['any-craps', 4]])
  for (const bet of sides) {
    if (firstTotal === undefined) {
      assert.equal(bet.resolved, false)
      assert.equal(bet.won, false)
      assert.equal(bet.totalReturn, null)
    } else {
      const expected = gross(bet.kind, firstTotal, bet.stake)
      assert.equal(bet.resolved, true)
      assert.equal(bet.won, expected > 0)
      assert.equal(cents(bet.totalReturn), cents(expected), `${bet.kind} first roll ${firstTotal}: incorrect gross return`)
    }
  }
}

const status = await request('status')
assert.equal(status.available, true)
assert.equal(status.minimumStake, 1)
assert.equal(status.maximumStake, 100)
assert.equal(status.stakeIncrement, 1)
assert.match(status.mode, /^local(?:-|$)/, 'Stop: this is not the disposable local sample mode.')

for (const stake of [0, -1, .5, 1.5, 100.5, 101]) await create({ stake }, 400)
for (const kind of ['field', 'any-seven', 'any-craps']) {
  for (const stake of [0, -1, .5, 1.5, 101]) await create({ stake: 10, extraBets: [{ kind, stake }] }, 400)
}
for (const kind of ['unknown', 'odds']) await create({ stake: 10, extraBets: [{ kind, stake: 1 }] }, 400)
await create({ stake: 10, extraBets: [{ kind: null, stake: 1 }] }, 400)
await create({ stake: 10, extraBets: [null] }, 400)
for (const stake of [1, 100]) {
  const round = await create({ stake })
  assert.equal(round.stake, stake)
  assert.equal(round.phase, 'come-out')
  assert.deepEqual(await read(round.roundId), round)
  const sides = await create({ stake: 10, extraBets: ['field', 'any-seven', 'any-craps'].map(kind => ({ kind, stake })) })
  assert.deepEqual(sides.extraBets.map(bet => [bet.kind, bet.stake, bet.resolved, bet.won, bet.totalReturn]),
    ['field', 'any-seven', 'any-craps'].map(kind => [kind, stake, false, false, null]))
  assert.deepEqual(await read(sides.roundId), sides)
}
const missing = '00000000-0000-4000-8000-000000000099'
await request(`rounds/${missing}`, { status: 404 })
await roll(missing, 404)
await odds(missing, 10, 404)

const totalsSeen = new Set()
const pointFamiliesSeen = new Set()
const winningOddsFamilies = new Set()
let oddsLosses = 0
let sessions = 0
let simultaneousOddsVerified = false

for (; sessions < maximumSessions; sessions += 1) {
  let round = await create({ stake: 10, extraBets: [{ kind: 'field', stake: 2 }, { kind: 'any-seven', stake: 3 }, { kind: 'any-craps', stake: 4 }] })
  checkRound(round)
  checkSideBets(round)
  assert.deepEqual(await read(round.roundId), round)
  await odds(round.roundId, 10, 400)
  assert.deepEqual(await read(round.roundId), round, 'Invalid come-out odds changed the round.')

  round = await roll(round.roundId)
  checkRound(round)
  const firstTotal = round.lastOutcome.total
  totalsSeen.add(firstTotal)
  checkSideBets(round, firstTotal)
  assert.deepEqual(await read(round.roundId), round)

  if (round.phase === 'point') {
    for (const invalid of [0, -1, .5, 1.5, 101]) await odds(round.roundId, invalid, 400)
    assert.deepEqual(await read(round.roundId), round, 'Invalid odds stakes changed the round.')
    const point = round.point
    const family = [4, 10].includes(point) ? '4/10' : [5, 9].includes(point) ? '5/9' : '6/8'
    const multiplier = family === '4/10' ? 3 : family === '5/9' ? 2.5 : 2.2
    const oddsStake = [1, 10, 100][sessions % 3]
    pointFamiliesSeen.add(family)
    if (!simultaneousOddsVerified) {
      const raced = await Promise.all([0, 1].map(() => request(`rounds/${round.roundId}/odds`, {
        method: 'POST', body: { stake: oddsStake }, status: [200, 400], includeStatus: true,
      })))
      assert.deepEqual(raced.map(result => result.status).sort(), [200, 400], 'Simultaneous odds requests must place exactly one bet.')
      round = raced.find(result => result.status === 200).value
      simultaneousOddsVerified = true
    } else round = await odds(round.roundId, oddsStake)
    checkSideBets(round, firstTotal)
    const placed = round.extraBets.filter(bet => bet.kind === 'odds')
    assert.equal(placed.length, 1)
    assert.deepEqual(placed[0], { kind: 'odds', stake: oddsStake, resolved: false, won: false, totalReturn: null })
    const afterPlacement = round
    await odds(round.roundId, 10, 400)
    assert.deepEqual(await read(round.roundId), afterPlacement, 'Duplicate odds changed the round.')

    let handRolls = 0
    while (round.phase !== 'resolved') {
      assert(handRolls++ < maximumRollsPerHand, 'Local hand exceeded the bounded roll budget.')
      const previousLength = round.rolls.length
      round = await roll(round.roundId)
      checkRound(round)
      assert.equal(round.rolls.length, previousLength + 1)
      checkSideBets(round, firstTotal)
      const currentOdds = round.extraBets.filter(bet => bet.kind === 'odds')
      assert.equal(currentOdds.length, 1)
      if (round.phase !== 'resolved') {
        assert.deepEqual(currentOdds[0], placed[0], 'Odds settled before the Pass Line resolved.')
      } else {
        const won = round.lastOutcome.result === 'point-hit'
        assert.equal(currentOdds[0].resolved, true)
        assert.equal(currentOdds[0].won, won)
        assert.equal(cents(currentOdds[0].totalReturn), cents(won ? oddsStake * multiplier : 0), `Point ${point}: incorrect gross odds return`)
        if (won) winningOddsFamilies.add(family)
        else oddsLosses += 1
      }
      assert.deepEqual(await read(round.roundId), round)
    }
  }

  const wonPass = ['natural-win', 'point-hit'].includes(round.lastOutcome.result)
  assert.equal(round.lastOutcome.totalReturn, wonPass ? 20 : 0)
  const terminal = await read(round.roundId)
  await roll(round.roundId, 409)
  await odds(round.roundId, 10, 400)
  assert.deepEqual(await read(round.roundId), terminal, 'Terminal requests changed a resolved hand.')

  // Stop early once random practice rolls have exercised every payout family.
  if (totalsSeen.size === 11 && winningOddsFamilies.size === 3 && oddsLosses > 0) { sessions += 1; break }
}

assert(pointFamiliesSeen.size > 0, 'No point-stage hand was exercised within the bounded sample.')
assert(simultaneousOddsVerified, 'The simultaneous odds placement check was not exercised.')
const unseenTotals = Array.from({ length: 11 }, (_, index) => index + 2).filter(total => !totalsSeen.has(total))
console.log(JSON.stringify({
  ok: true, sampleOrigin: origin.origin, sessions, rolls, requests, simultaneousOddsVerified,
  firstRollTotals: [...totalsSeen].sort((a, b) => a - b),
  pointFamilies: [...pointFamiliesSeen].sort(), winningOddsFamilies: [...winningOddsFamilies].sort(), oddsLosses,
  randomCoverageNote: unseenTotals.length || winningOddsFamilies.size < 3
    ? `Bounded random sample did not observe totals ${unseenTotals.join(', ') || '(none)'}; winning odds families ${['4/10', '5/9', '6/8'].filter(family => !winningOddsFamilies.has(family)).join(', ') || '(none)'}. Every observed return was verified.`
    : 'All first-roll totals and all winning/loss odds payout families were observed and verified.',
}, null, 2))

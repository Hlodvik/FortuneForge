import test from 'node:test'
import assert from 'node:assert/strict'
import { decodeRoadFeature, decodeRoadState, roadPaths, roadTilePorts } from '../src/road.ts'

const tile = (id, kind = 'straight', rotation = 0) => ({ id, kind, rotation })
const empty = () => Array(9).fill(null)
const state = () => ({
  contractVersion: 1, revision: 0, board: empty(), hand: [tile('ready')], queuedTiles: [],
  reserveTileCounts: { straight: 0, corner: 0, junction: 0 },
  funding: { spins: 0, requiredSpins: 20, wagerPointsTotal: 0 }, tickets: [], activeBonus: null,
  reachableDestinations: [], lastTravel: null, completedJourneys: 0,
})

test('rotates asymmetric junction ports clockwise', () => {
  assert.deepEqual(roadTilePorts(tile('a', 'junction', 0)), ['north', 'east', 'west'])
  assert.deepEqual(roadTilePorts(tile('a', 'junction', 1)), ['north', 'east', 'south'])
  assert.deepEqual(roadTilePorts(tile('a', 'junction', 2)), ['east', 'south', 'west'])
  assert.deepEqual(roadTilePorts(tile('a', 'junction', 3)), ['north', 'south', 'west'])
})

test('finds the southern entrance to northern fort through reciprocal edges', () => {
  const board = empty()
  for (const cell of [7, 4, 1]) board[cell] = tile(String(cell))
  assert.deepEqual(roadPaths(board), { fort: [7, 4, 1] })
  board[4] = tile('4', 'straight', 1)
  assert.deepEqual(roadPaths(board), {})
})

test('finds bent western and eastern destination routes', () => {
  const west = empty()
  west[7] = tile('7', 'corner', 2)
  west[6] = tile('6', 'corner', 0)
  west[3] = tile('3', 'corner', 2)
  assert.deepEqual(roadPaths(west), { well: [7, 6, 3] })
  const east = empty()
  east[7] = tile('7', 'corner', 1)
  east[8] = tile('8', 'corner', 3)
  east[5] = tile('5', 'corner', 1)
  assert.deepEqual(roadPaths(east), { hill: [7, 8, 5] })
})

test('rejects a path that never connects the southern entrance', () => {
  const board = empty()
  for (const cell of [7, 4, 1]) board[cell] = tile(String(cell))
  board[7] = tile('7', 'corner', 0)
  assert.deepEqual(roadPaths(board), {})
})

test('validates a public server snapshot without supplying missing fields', () => {
  const valid = state()
  assert.equal(decodeRoadState(valid), valid)
  assert.equal(decodeRoadState({ ...valid, contractVersion: 2 }), null)
  assert.equal(decodeRoadState({ ...valid, board: [null] }), null)
  assert.equal(decodeRoadState({ ...valid, reserveTileCounts: null }), null)
  assert.equal(decodeRoadState({ ...valid, funding: { ...valid.funding, spins: 20 } }), null)
  assert.equal(decodeRoadState({ ...valid, hand: [tile('same'), tile('same')] }), null)
})

test('validates door stages and excludes malformed hidden prize data', () => {
  const valid = state()
  valid.activeBonus = { id: 'bonus', kind: 'hill', step: 0, totalSteps: 3, awardPoints: 0,
    fundedWagerPoints: 300, choice: null, board: null, held: null, respinsRemaining: 0,
    doors: [0, 1, 2].map(index => ({ index, selected: false, revealed: false, prizePoints: null })) }
  assert.equal(decodeRoadState(valid), valid)
  assert.equal(decodeRoadFeature({ kind: 'hill', completed: true, bonus: valid.activeBonus })?.completed, true)
  assert.equal(decodeRoadFeature({ kind: 'well', completed: true, bonus: valid.activeBonus }), null)
  valid.activeBonus.doors[0].prizePoints = -1
  assert.equal(decodeRoadState(valid), null)
})

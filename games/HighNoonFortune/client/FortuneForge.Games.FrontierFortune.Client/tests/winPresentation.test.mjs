import test from 'node:test'
import assert from 'node:assert/strict'
import { settledFrontierWin } from '../dist/winPresentation.js'

const line = { line: 1, symbol: 'J', count: 3, awardPoints: 8, cells: [{ column: 0, row: 0 }, { column: 1, row: 0 }, { column: 2, row: 0 }] }
const state = change => ({ revision: 20, lastWinPoints: 8, wins: [line], handRound: null, pendingOperation: null, ...change })
const hand = change => ({ roundId: 'earned-hand', wagerPoints: 4, dealsRemaining: 0, phase: 'complete',
  hand: [], awardPoints: 240, category: 'five', ...change })

test('line celebration uses the settled server total and exact reported winning cells', () => {
  const wins = [line, { ...line, line: 2, awardPoints: 4 }]
  const win = settledFrontierWin(state({ lastWinPoints: 12, wins }))
  assert.equal(win.kind, 'lines')
  assert.equal(win.points, 12)
  assert.deepEqual(win.wins, wins)
})

test('only a completed and matching hand award produces a feature celebration', () => {
  const complete = state({ lastWinPoints: 240, wins: [], handRound: hand() })
  assert.equal(settledFrontierWin(complete).kind, 'hand')
  assert.equal(settledFrontierWin(complete).category, 'five')
  assert.equal(settledFrontierWin(complete).points, 240)
  assert.deepEqual(settledFrontierWin(complete).wins, [])
  assert.equal(settledFrontierWin({ ...complete, handRound: hand({ phase: 'hold' }) }), null)
  assert.equal(settledFrontierWin({ ...complete, lastWinPoints: 120 }), null)
})

test('starting the next spin suppresses stale reloaded or replayed receipts but allows its new award', () => {
  assert.equal(settledFrontierWin(state(), 20), null)
  assert.equal(settledFrontierWin(state({ revision: 19 }), 20), null)
  assert.equal(settledFrontierWin(state({ revision: 21 }), 20).points, 8)
})

test('pending settlement, losses and missing win reports do not create celebrations', () => {
  assert.equal(settledFrontierWin(state({ pendingOperation: { kind: 'spin' } })), null)
  assert.equal(settledFrontierWin(state({ lastWinPoints: 0 })), null)
  assert.equal(settledFrontierWin(state({ wins: [] })), null)
})

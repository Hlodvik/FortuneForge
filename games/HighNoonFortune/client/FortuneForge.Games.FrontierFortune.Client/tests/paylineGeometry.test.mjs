import assert from 'node:assert/strict'
import test from 'node:test'
import { winTraceGeometry, winTracePoints } from '../dist/paylineGeometry.js'

test('a three-cell win stops at its last server cell rather than extending across five reels', () => {
  const win = { line: 4, symbol: 'J', count: 3, awardPoints: 2,
    cells: [{ column: 0, row: 0 }, { column: 1, row: 1 }, { column: 2, row: 2 }] }
  assert.equal(winTracePoints(win), '100,100 300,300 500,500')
  assert.equal(winTraceGeometry(win).cells.length, 3)
})

test('server cell order is preserved and unrelated payline metadata cannot change a trace', () => {
  const cells = [{ column: 4, row: 2 }, { column: 1, row: 0 }, { column: 0, row: 1 }]
  assert.equal(winTracePoints({ line: 1, count: 5, cells }), '900,500 300,100 100,300')
  assert.equal(winTracePoints({ line: 20, count: 3, cells }), '900,500 300,100 100,300')
})

test('geometry leaves settled cells unchanged and creates no positions for an empty report', () => {
  const cells = Object.freeze([Object.freeze({ column: 2, row: 1 })])
  assert.deepEqual(winTraceGeometry({ cells }), {
    cells: [{ column: 2, row: 1, x: 500, y: 300 }], points: '500,300',
  })
  assert.deepEqual(cells, [{ column: 2, row: 1 }])
  assert.deepEqual(winTraceGeometry({ cells: [] }), { cells: [], points: '' })
})

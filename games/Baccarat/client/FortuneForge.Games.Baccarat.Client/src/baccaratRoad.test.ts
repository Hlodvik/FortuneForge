import { describe, expect, it } from 'vitest'
import { buildBigRoad, type BaccaratHistoryItem } from './baccaratRoad'

const history = (outcomes: string): BaccaratHistoryItem[] => [...outcomes].map<BaccaratHistoryItem>((outcome, index) => ({
  roundId: String(index), outcome: outcome === 'B' ? 'banker' : outcome === 'P' ? 'player' : 'tie', cardsUsed: 4, natural: false,
})).reverse()

describe('Big Road presentation', () => {
  it('starts a new streak beside its starting column and bends above an existing dragon tail', () => {
    const cells = buildBigRoad(history('BBBBBBBPPPPPPBB'))
    expect(cells.slice(0, 7).map(({ column, row }) => [column, row])).toEqual([[0, 0], [0, 1], [0, 2], [0, 3], [0, 4], [0, 5], [1, 5]])
    expect(cells.slice(7, 13).map(({ column, row }) => [column, row])).toEqual([[1, 0], [1, 1], [1, 2], [1, 3], [1, 4], [2, 4]])
    expect(cells.slice(13).map(({ column, row }) => [column, row])).toEqual([[2, 0], [2, 1]])
    expect(new Set(cells.map(cell => cell.column + ',' + cell.row)).size).toBe(cells.length)
  })
  it('retains leading and intervening ties without adding a streak column', () => {
    const cells = buildBigRoad(history('TTBTTBP'))
    expect(cells).toEqual([
      { outcome: 'banker', column: 0, row: 0, ties: 4 },
      { outcome: 'banker', column: 0, row: 1, ties: 0 },
      { outcome: 'player', column: 1, row: 0, ties: 0 },
    ])
  })
  it('keeps the latest tail within the twelve visible columns', () => {
    const cells = buildBigRoad(history('B'.repeat(30)))
    expect(cells.every(cell => cell.column >= 0 && cell.column < 12 && cell.row < 6)).toBe(true)
    expect(cells.some(cell => cell.column === 11 && cell.row === 5)).toBe(true)
  })
  it('keeps the newest opposite result when an old tail extends beyond the visible window', () => {
    const cells = buildBigRoad(history('B'.repeat(30) + 'P'))
    expect(cells.at(-1)).toEqual({ outcome: 'player', column: 1, row: 0, ties: 0 })
    expect(cells.every(cell => cell.column >= 0 && cell.column < 12)).toBe(true)
  })
})

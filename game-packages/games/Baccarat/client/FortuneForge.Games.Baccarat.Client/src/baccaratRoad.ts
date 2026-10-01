import type { BaccaratRoundOutcome } from './contracts'

export type BaccaratHistoryItem = Readonly<{ roundId: string; outcome: BaccaratRoundOutcome; cardsUsed: number; natural: boolean }>
type RoadCell = { outcome: 'player' | 'banker'; column: number; row: number; ties: number }

// Six-row Big Road: a new streak starts in the next starting column,
// while a continued streak bends right at the bottom or an occupied cell.
export function buildBigRoad(history: readonly BaccaratHistoryItem[]): readonly RoadCell[] {
  const cells: RoadCell[] = []
  let startColumn = -1, column = 0, row = 0, tail = false, leadingTies = 0
  const occupied = (x: number, y: number) => cells.some(cell => cell.column === x && cell.row === y)
  for (const item of [...history].reverse()) {
    const previous = cells.at(-1)
    if (item.outcome === 'tie') {
      if (previous) previous.ties++
      else leadingTies++
      continue
    }
    if (!previous || previous.outcome !== item.outcome) {
      startColumn++
      column = startColumn; row = 0; tail = false
    } else if (!tail && row < 5 && !occupied(column, row + 1)) {
      row++
    } else {
      tail = true
      column++
    }
    while (occupied(column, row)) column++
    cells.push({ outcome: item.outcome, column, row, ties: previous ? 0 : leadingTies })
  }
  const offset = Math.max(0, (cells.at(-1)?.column ?? 0) - 11)
  return cells.filter(cell => cell.column >= offset && cell.column < offset + 12).map(cell => ({ ...cell, column: cell.column - offset }))
}

import type { BaccaratRoundOutcome } from './contracts'

export type BaccaratHistoryItem = Readonly<{ roundId: string; outcome: BaccaratRoundOutcome; cardsUsed: number; natural: boolean }>
type RoadCell = { outcome: 'player' | 'banker'; column: number; row: number; ties: number }

export type BaccaratDerivedRoadKind = 'bigEyeBoy' | 'smallRoad' | 'cockroachPig'
export type BaccaratDerivedRoadCell = Readonly<{ color: 'red' | 'blue'; column: number; row: number }>

const comparisonDistance: Record<BaccaratDerivedRoadKind, number> = {
  bigEyeBoy: 1,
  smallRoad: 2,
  cockroachPig: 3,
}

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

// The derived roads describe the shape of the *unclipped* Big Road, not the
// winning side. Ties do not advance any road. Streak depths are used rather
// than rendered rows so dragon tails behave as though they continue downward.
export function buildDerivedRoad(
  history: readonly BaccaratHistoryItem[],
  kind: BaccaratDerivedRoadKind,
  visibleColumns = 12,
): readonly BaccaratDerivedRoadCell[] {
  const distance = comparisonDistance[kind]
  const streaks: { outcome: 'player' | 'banker'; depth: number }[] = []
  const colors: ('red' | 'blue')[] = []

  for (const item of [...history].reverse()) {
    if (item.outcome === 'tie') continue
    const last = streaks.at(-1)
    if (!last || last.outcome !== item.outcome) {
      const index = streaks.length
      if (index > distance) {
        colors.push(streaks[index - 1].depth === streaks[index - distance - 1].depth ? 'red' : 'blue')
      }
      streaks.push({ outcome: item.outcome, depth: 1 })
      continue
    }

    last.depth++
    const index = streaks.length - 1
    if (index < distance) continue
    const comparisonDepth = streaks[index - distance].depth
    const atCurrentDepth = comparisonDepth >= last.depth
    const atPreviousDepth = comparisonDepth >= last.depth - 1
    colors.push(atCurrentDepth === atPreviousDepth ? 'red' : 'blue')
  }

  const cells: BaccaratDerivedRoadCell[] = []
  let startColumn = -1, column = 0, row = 0, tail = false
  const occupied = (x: number, y: number) => cells.some(cell => cell.column === x && cell.row === y)
  for (const color of colors) {
    const previous = cells.at(-1)
    if (!previous || previous.color !== color) {
      startColumn++
      column = startColumn; row = 0; tail = false
    } else if (!tail && row < 5 && !occupied(column, row + 1)) {
      row++
    } else {
      tail = true
      column++
    }
    while (occupied(column, row)) column++
    cells.push({ color, column, row })
  }

  const width = Math.max(1, Math.floor(visibleColumns))
  const offset = Math.max(0, (cells.at(-1)?.column ?? 0) - width + 1)
  return cells.filter(cell => cell.column >= offset && cell.column < offset + width)
    .map(cell => ({ ...cell, column: cell.column - offset }))
}

export function buildDerivedRoads(history: readonly BaccaratHistoryItem[], visibleColumns = 12): Readonly<Record<BaccaratDerivedRoadKind, readonly BaccaratDerivedRoadCell[]>> {
  return {
    bigEyeBoy: buildDerivedRoad(history, 'bigEyeBoy', visibleColumns),
    smallRoad: buildDerivedRoad(history, 'smallRoad', visibleColumns),
    cockroachPig: buildDerivedRoad(history, 'cockroachPig', visibleColumns),
  }
}

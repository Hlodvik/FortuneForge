import { FRONTIER_RULES } from './rules.js'
import type { FrontierWin } from './types.js'

export const PAYLINE_VIEWBOX = { width: 1000, height: 600 } as const

export type PaylineTraceCell = { column: number; row: number; x: number; y: number }
export type PaylineTraceGeometry = { cells: PaylineTraceCell[]; points: string }

/** Project exactly the settled cells; line IDs and counts never supply extra positions. */
export function winTraceGeometry(win: Pick<FrontierWin, 'cells'>): PaylineTraceGeometry {
  const cells = win.cells.map(({ column, row }) => ({
    column,
    row,
    x: (column + 0.5) * PAYLINE_VIEWBOX.width / FRONTIER_RULES.columns,
    y: (row + 0.5) * PAYLINE_VIEWBOX.height / FRONTIER_RULES.rows,
  }))
  return { cells, points: cells.map(({ x, y }) => `${x},${y}`).join(' ') }
}

export function winTracePoints(win: Pick<FrontierWin, 'cells'>): string {
  return winTraceGeometry(win).points
}

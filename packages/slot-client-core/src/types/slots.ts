/** Render-only contracts. Each game owns its result and state contracts. */
export type SlotSymbolId = string
export type GridPosition = Readonly<{ reel: number; row: number }>
export type LightningCollection = Readonly<{
  collector: GridPosition
  cells: readonly (GridPosition & Readonly<{ amountPoints: number }>)[]
  totalPoints: number
}>
export type PaylinePayout = Readonly<{
  paylineId: number
  amountPoints: number
  matches: readonly Readonly<{ amountPoints: number; match: Readonly<{ positions: readonly GridPosition[] }> }>[]
}>

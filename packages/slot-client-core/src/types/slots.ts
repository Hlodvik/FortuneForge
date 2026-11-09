/** Render-only contracts. Each game owns its result and state contracts. */
export type SlotSymbolId = string
export type GridPosition = Readonly<{ reel: number; row: number }>
export type PaylinePayout = Readonly<{
  paylineId: number
  amountPoints: number
  matches: readonly Readonly<{ amountPoints: number; match: Readonly<{ positions: readonly GridPosition[] }> }>[]
}>

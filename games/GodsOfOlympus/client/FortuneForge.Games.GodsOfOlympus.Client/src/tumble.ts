import type { GridPosition, SlotSymbolId } from '@fortuneforge/slot-client-core'

export const GODS_TUMBLE_COLUMNS = 6
export const GODS_TUMBLE_ROWS = 5
export const GODS_TUMBLE_GAME_ID = 'gods-of-olympus-v1'
export const GODS_TUMBLE_MULTIPLIERS = { X2: 2, X3: 3, X5: 5, X10: 10 } as const
const BASE_SYMBOLS: readonly SlotSymbolId[] = ['2', '3', '4', '5', '6', '7', '8', 'A', 'J']
export const GODS_TUMBLE_SYMBOLS: readonly SlotSymbolId[] = [
  ...BASE_SYMBOLS, ...Object.keys(GODS_TUMBLE_MULTIPLIERS),
]

export const GODS_TUMBLE_INITIAL_REELS = Array.from({ length: GODS_TUMBLE_COLUMNS }, (_, reel) =>
  Array.from({ length: GODS_TUMBLE_ROWS }, (_, row) =>
    BASE_SYMBOLS[(reel * GODS_TUMBLE_ROWS + row) % BASE_SYMBOLS.length]),
)

export type GodsTumbleAward = {
  symbol: SlotSymbolId
  amountPoints: number
  positions: GridPosition[]
}

export type GodsTumbleStep = {
  awards: GodsTumbleAward[]
  cleared: GridPosition[]
  reels: SlotSymbolId[][]
  dropRows: number[][]
  amountPoints: number
}

export type GodsTumbleResult = {
  initialReels: SlotSymbolId[][]
  steps: GodsTumbleStep[]
  totalPoints: number
  baseTotalPoints?: number
  multiplier?: number
}

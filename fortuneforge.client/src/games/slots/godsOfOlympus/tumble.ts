import type { GridPosition, SlotSymbolId } from '../../../features/slots/types/slots'

export const GODS_TUMBLE_COLUMNS = 6
export const GODS_TUMBLE_ROWS = 5
export const GODS_TUMBLE_GAME_ID = 'gods-of-olympus-v1'
export const GODS_TUMBLE_SYMBOLS: readonly SlotSymbolId[] = ['2', '3', '4', '5', '6', '7', '8', 'A', 'J']

export const GODS_TUMBLE_INITIAL_REELS = Array.from({ length: GODS_TUMBLE_COLUMNS }, (_, reel) =>
  Array.from({ length: GODS_TUMBLE_ROWS }, (_, row) =>
    GODS_TUMBLE_SYMBOLS[(reel * GODS_TUMBLE_ROWS + row) % GODS_TUMBLE_SYMBOLS.length]),
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
}

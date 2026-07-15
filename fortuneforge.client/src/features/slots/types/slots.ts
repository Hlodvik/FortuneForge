export const slotSymbolIds = ['2', '3', '4', '5', '6', '7', 'ACE'] as const

export type SlotSymbolId = (typeof slotSymbolIds)[number]

export type SpinResult = {
  spinId: string
  gameId: string
  reelSetId: string
  symbolSetId: string
  paytableId: string
  wagerPoints: number
  pointValueInCents: number
  reelStops: number[]
  reels: SlotSymbolId[][]
  payout: {
    totalPoints: number
  }
}

export function isSlotSymbolId(value: unknown): value is SlotSymbolId {
  return typeof value === 'string' && slotSymbolIds.some((symbolId) => symbolId === value)
}

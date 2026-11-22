// Edited: 2026-11-18T19:58:40-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:00:18-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T14:52:25-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:30:40-06:00 | Computer: Vinnabok
import type { SlotPaidModeState, SlotPotionState, SlotTomeState } from '@fortuneforge/slot-game-runtime/experience'
import type { SlotSymbolId as SharedSlotSymbolId } from '@fortuneforge/slot-client-core'
import type { GodsTumbleResult } from '@fortuneforge/games-gods-of-olympus/tumble'
export type { GodsTumbleAward, GodsTumbleStep, GodsTumbleResult } from '@fortuneforge/games-gods-of-olympus/tumble'

export type SlotSymbolId = SharedSlotSymbolId

export const slotSymbolIds: readonly SlotSymbolId[] = [
  '2',
  '3',
  '4',
  '5',
  '6',
  '7',
  '8',
  'A',
  'J',
  'K',
  'Q',
  'ACE',
  'FREE',
  'POWER',
  'BOLT',
  'BANANA',
  'WAND',
  'FORTUNE_RUNE',
  'PAW',
  'RAND_05',
  'RAND_1',
  'RAND_15',
  'RAND_2',
  'RAND_3',
  'RAND_4',
  'RAND_5',
  'SEAL_SYNC',
  'SEAL_ROWS',
  'SEAL_PAW',
  'SEAL_RAND',
]

export type GridPosition = {
  reel: number
  row: number
}

export type LightningCollection = {
  collector: GridPosition
  cells: (GridPosition & { amountPoints: number })[]
  totalPoints: number
}

export const arcaneWandPaylineId = 903

export type ArcaneWandCollection = {
  wand: GridPosition
  cells: (GridPosition & { symbolId: SlotSymbolId; multiplier: number; amountPoints: number; potionBonusPoints?: number })[]
  totalPoints: number
}

export type SymbolMatch = {
  paylineId: number
  symbolId: SlotSymbolId
  matchLength: number
  positions: GridPosition[]
  wildPositions: GridPosition[]
}

export type PaidMatch = {
  match: SymbolMatch
  multiplier: number
  amountPoints: number
}

export type PaylinePayout = {
  paylineId: number
  amountPoints: number
  matches: PaidMatch[]
}

export type SlotSealCollection = {
  sealId: string
  count: number
  averageWagerPoints: number
  requiredCount: number
}

export type SpinResult = {
  paidMode?: SlotPaidModeState | null
  /** Optional, versioned result contract owned and validated by the selected game. */
  spinFeature?: unknown
  olympusTumble?: GodsTumbleResult
  spinId: string
  gameId: string
  reelSetId: string
  symbolSetId: string
  paytableId: string
  wagerPoints: number
  pointValueInCents: number
  chargedWagerCents?: number
  reelStops: number[]
  reels: SlotSymbolId[][]
  consecutiveFiveMisses: number
  fiveMatchPityTriggered: boolean
  isFreeSpin: boolean
  freeSpinsAwarded: number
  collectionFreeSpinsAwarded?: number
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  specialPointsAwarded: number
  specialPointsBalance: number
  energyAwarded: number
  energyBalance: number
  energyMultiplierApplied: boolean
  payoutMultiplier: number
  monkeyPawCount: number
  moneyGrabPoints: number
  lightningCollection?: LightningCollection | null
  arcaneWandCollection?: ArcaneWandCollection | null
  arcaneTome?: SlotTomeState | null
  arcanePotions?: SlotPotionState | null
  arcaneIngredientsAwarded?: Readonly<Record<string, number>> | null
  bananaBonusPoints: number
  sealsAwarded: Record<string, number>
  sealCollections: SlotSealCollection[]
  freeSpinFeatureMode: string | null
  specialBoostApplied: boolean
  slotsCreditsBalance: number | null
  payout: {
    totalPoints: number
    paylines: PaylinePayout[]
  }
}

export function isSlotSymbolId(value: unknown): value is SlotSymbolId {
  return typeof value === 'string' && slotSymbolIds.some((symbolId) => symbolId === value)
}

import type { SlotSymbolId as SharedSlotSymbolId } from '@fortuneforge/slot-client-core'

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

export type SlotTumbleRules = Readonly<{
  columns: number
  rows: number
  symbols: readonly SlotSymbolId[]
  initialReels: readonly (readonly SlotSymbolId[])[]
}>

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

export type LightningCollection = {
  collector: GridPosition
  cells: (GridPosition & { amountPoints: number })[]
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

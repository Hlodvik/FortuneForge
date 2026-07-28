export const slotSymbolIds = [
  '2',
  '3',
  '4',
  '5',
  '6',
  '7',
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
] as const

export type SlotSymbolId = (typeof slotSymbolIds)[number]

export type GridPosition = {
  reel: number
  row: number
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
  spinId: string
  gameId: string
  reelSetId: string
  symbolSetId: string
  paytableId: string
  wagerPoints: number
  pointValueInCents: number
  reelStops: number[]
  reels: SlotSymbolId[][]
  consecutiveFiveMisses: number
  fiveMatchPityTriggered: boolean
  isFreeSpin: boolean
  freeSpinsAwarded: number
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

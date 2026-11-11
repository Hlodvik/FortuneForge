import type { ReactNode } from 'react'
import type { GridPosition } from '@fortuneforge/slot-client-core'

export const GOLD_FOUNDRY_GAME_ID = 'gold-foundry-v1'
export const GOLD_FOUNDRY_SYMBOL_SET_ID = 'gold-foundry-symbols-v1'
export const GOLD_FOUNDRY_PAYTABLE_ID = 'gold-foundry-paytable-v1'
export const goldFoundrySymbolIds = [
  '2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BOLT', 'BANANA', 'PAW',
  'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND',
] as const

export type GoldFoundrySymbolId = typeof goldFoundrySymbolIds[number]
export type GoldFoundryAccount = { userId: string; role: string; balances: { slotsCredits: number } }
export type GoldFoundryAuthenticatedFetch = (path: string, init?: RequestInit) => Promise<Response>
export type GoldFoundryShellProps = { balance: number; onNavigate: () => void; children: ReactNode }
export type GoldFoundryGameProps = {
  account?: GoldFoundryAccount
  demoMode?: boolean
  authenticatedFetch?: GoldFoundryAuthenticatedFetch
  renderShell?: (props: GoldFoundryShellProps) => ReactNode
  paymentAlerts?: ReactNode
  onSpinStateChange?: (isSpinning: boolean) => void
}

export type GoldFoundryState = {
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  specialPointsBalance: number
  energyBalance: number
  sealCollections: readonly never[]
  freeSpinFeatureMode: null
  crucibleWageredCents: number
  cruciblePourValueCents: number
}

export type GoldFoundryMatch = {
  paylineId: number
  symbolId: GoldFoundrySymbolId
  matchLength: number
  positions: GridPosition[]
  wildPositions: GridPosition[]
}
export type GoldFoundryPaidMatch = { match: GoldFoundryMatch; multiplier: number; amountPoints: number }
export type GoldFoundryPaylinePayout = { paylineId: number; amountPoints: number; matches: GoldFoundryPaidMatch[] }

export type GoldFoundrySpinResult = {
  spinId: string
  gameId: typeof GOLD_FOUNDRY_GAME_ID
  reelSetId: 'gold-foundry-reels-v1'
  symbolSetId: typeof GOLD_FOUNDRY_SYMBOL_SET_ID
  paytableId: typeof GOLD_FOUNDRY_PAYTABLE_ID
  wagerPoints: number
  pointValueInCents: 25
  chargedWagerCents?: number
  reelStops: number[]
  reels: GoldFoundrySymbolId[][]
  consecutiveFiveMisses: number
  fiveMatchPityTriggered: boolean
  isFreeSpin: false
  freeSpinsAwarded: 0
  freeSpinsRemaining: 0
  freeSpinWagerPoints: null
  specialPointsAwarded: number
  specialPointsBalance: number
  energyAwarded: number
  energyBalance: number
  energyMultiplierApplied: false
  crucibleWageredCents?: number
  cruciblePourValueCents?: number
  cruciblePoured?: boolean
  cruciblePayoutCents?: number
  payoutMultiplier: number
  monkeyPawCount: number
  moneyGrabPoints: number
  bananaBonusPoints: number
  sealsAwarded: Record<string, number>
  sealCollections: readonly never[]
  freeSpinFeatureMode: null
  specialBoostApplied: false
  slotsCreditsBalance: number | null
  payout: { totalPoints: number; paylines: GoldFoundryPaylinePayout[] }
}

export type GoldFoundrySpinRequest = {
  wagerPoints: number
  state: GoldFoundryState
}

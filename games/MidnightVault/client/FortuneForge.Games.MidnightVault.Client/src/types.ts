import type { ReactNode } from 'react'
import type { GridPosition } from '@fortuneforge/slot-client-core'

export const MIDNIGHT_GAME_ID = 'dragon-hoard-v1'
export const MIDNIGHT_SYMBOL_SET_ID = 'midnight-vault-symbols-v1'
export const midnightSymbolIds = [
  '2', '3', '4', '5', '6', '7', '8', 'ACE', 'FREE', 'POWER', 'BANANA',
  'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND',
] as const
export const midnightMathModelIds = [
  'midnight-vault-paytable-v1',
  'midnight-vault-paytable-v2',
  'midnight-vault-paytable-v3',
] as const

export type MidnightSymbolId = typeof midnightSymbolIds[number]
export type MidnightMathModelId = typeof midnightMathModelIds[number]
export type MidnightCollectionId = 'sync' | 'rows' | 'paw' | 'rand'
export type MidnightAccount = { userId: string; role: string; balances: { slotsCredits: number } }
export type MidnightAuthenticatedFetch = (path: string, init?: RequestInit) => Promise<Response>
export type MidnightShellProps = { balance: number; onNavigate: () => void; children: ReactNode }
export type MidnightVaultGameProps = {
  account?: MidnightAccount
  demoMode?: boolean
  authenticatedFetch?: MidnightAuthenticatedFetch
  renderShell?: (props: MidnightShellProps) => ReactNode
  paymentAlerts?: ReactNode
  onSpinStateChange?: (isSpinning: boolean) => void
}

export type MidnightCollection = {
  sealId: MidnightCollectionId
  count: number
  averageWagerPoints: number
  requiredCount: number
}

export type MidnightBonusRound = {
  id: string
  totalSpins: number
  remainingSpins: number
  wagerPoints: number
  featureMode: string | null
  mathModelId: MidnightMathModelId
}

export type MidnightDemoState = {
  rounds: MidnightBonusRound[]
  collectionCounts: Record<MidnightCollectionId, number>
  collectionWagerPoints: Record<MidnightCollectionId, number>
}

export type MidnightState = {
  bonusRound: MidnightBonusRound | null
  queuedBonusRounds: MidnightBonusRound[]
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  freeSpinFeatureMode: string | null
  specialPointsBalance: number
  energyBalance: number
  sealCollections: MidnightCollection[]
}

export type MidnightMatch = {
  paylineId: number
  symbolId: MidnightSymbolId
  matchLength: number
  positions: GridPosition[]
  wildPositions: GridPosition[]
}
export type MidnightPaidMatch = { match: MidnightMatch; multiplier: number; amountPoints: number }
export type MidnightPaylinePayout = { paylineId: number; amountPoints: number; matches: MidnightPaidMatch[] }

export type MidnightSpinResult = MidnightState & {
  spinId: string
  gameId: typeof MIDNIGHT_GAME_ID
  reelSetId: string
  symbolSetId: typeof MIDNIGHT_SYMBOL_SET_ID
  paytableId: MidnightMathModelId
  wagerPoints: number
  pointValueInCents: 25
  chargedWagerCents?: number
  reelStops: number[]
  reels: MidnightSymbolId[][]
  consecutiveFiveMisses: number
  fiveMatchPityTriggered: boolean
  isFreeSpin: boolean
  freeSpinsAwarded: number
  collectionFreeSpinsAwarded?: number
  awardedBonusRounds: MidnightBonusRound[]
  midnightBonusState: MidnightDemoState | null
  specialPointsAwarded: number
  energyAwarded: number
  energyMultiplierApplied: boolean
  payoutMultiplier: number
  monkeyPawCount: number
  moneyGrabPoints: number
  bananaBonusPoints: number
  sealsAwarded: Record<string, number>
  specialBoostApplied: boolean
  slotsCreditsBalance: number | null
  payout: { totalPoints: number; paylines: MidnightPaylinePayout[] }
}

export type MidnightSpinRequest = {
  wagerPoints: number
  useFreeSpin: boolean
  state: MidnightState
  demoState: MidnightDemoState | null
}

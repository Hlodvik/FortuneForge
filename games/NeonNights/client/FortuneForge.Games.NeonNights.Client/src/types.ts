import type { ReactNode } from 'react'
import type { GridPosition, PaylinePayout } from '@fortuneforge/slot-client-core'

export const NEON_GAME_ID = 'neon-nights-v1'
export const NEON_CAR_SYMBOL_SET_ID = 'neon-car-symbols-v1'
export const NEON_DISCO_SYMBOL_SET_ID = 'neon-disco-symbols-v1'
export const discoMultipliers = [1, 2, 3, 5] as const
export type NeonDiscoMultiplier = typeof discoMultipliers[number]
export const neonBaseSymbolIds = ['2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BANANA', 'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW'] as const
export const neonCarSymbolIds = [...neonBaseSymbolIds, 'NITRO'] as const
export const neonDiscoSymbolIds = ['2', '4', '5', '7', 'DISCO_MARTINI', 'DISCO_BALL', 'DISCO_SEVEN'] as const
export const neonSymbolIds = [...neonCarSymbolIds, 'DISCO_MARTINI', 'DISCO_BALL', 'DISCO_SEVEN'] as const
export type NeonSymbolId = typeof neonSymbolIds[number]
export type NeonCarSymbolId = typeof neonCarSymbolIds[number]
export type NeonDiscoSymbolId = typeof neonDiscoSymbolIds[number]
export type NeonTriggerSymbol = '6' | 'POWER'
export type NeonAccount = { userId: string; role: string; balances: { slotsCredits: number } }
export type NeonAuthenticatedFetch = (path: string, init?: RequestInit) => Promise<Response>
export type NeonShellProps = { balance: number; onNavigate: () => void; children: ReactNode }
export type NeonNightsGameProps = {
  account?: NeonAccount
  demoMode?: boolean
  authenticatedFetch?: NeonAuthenticatedFetch
  renderShell?: (props: NeonShellProps) => ReactNode
  onSpinStateChange?: (isSpinning: boolean) => void
}
export type NeonState = {
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  freeSpinFeatureMode: string | null
  neonDiscoSpinsRemaining: number
  neonDiscoMultiplier: NeonDiscoMultiplier
  neonDiscoRoundWinPoints?: number | null
  neonCarRespinOffer?: NeonCarRespinOffer | null
  neonCarRoundWinPoints?: number | null
  pendingNeonNitroChoice?: NeonNitroChoice | null
}
export type NeonCarRespinOffer = {
  spinId: string
  eligibleReels: number[]
  reels: NeonCarSymbolId[][]
  roundWinPoints: number
}
export type NeonCascadeStep = {
  multiplier: NeonDiscoMultiplier
  cleared: GridPosition[]
  reels: NeonDiscoSymbolId[][]
  dropRows: number[][]
  payout: { totalPoints: number; paylines: PaylinePayout[] }
}
export type NeonNitroWild = {
  trigger: GridPosition
  secondNitro?: GridPosition | null
  eligibleReel: number
  expandedReel: number | null
  initialReels: NeonCarSymbolId[][]
  requiresChoice?: boolean
}
type NitroChoiceBase = {
  spinId: string
  wagerPoints: number
  freeSpinsRemaining: number
  initialReels: NeonCarSymbolId[][]
  eligibleReels: number[]
  neonNitroWild: NeonNitroWild
}
export type NeonNitroChoice = NitroChoiceBase & (
  { sourceKind: 'spin' } | { sourceKind: 'car-respin'; reelIndex: number; roundWinPoints: number }
)
export type NeonPendingChoiceResponse = { pendingNeonNitroChoice: NeonNitroChoice }
export type NeonCarRespinResult = {
  sourceSpinId: string
  reelIndex: number
  stopIndex: number
  initialReels: NeonCarSymbolId[][]
  reels: NeonCarSymbolId[][]
  payout: { totalPoints: number; paylines: PaylinePayout[] }
  originalPayoutPoints: number
  improvementPoints: number
  roundWinPoints?: number | null
  slotsCreditsBalance: number
  alreadyApplied: boolean
  neonNitroWild?: NeonNitroWild | null
}
export type NeonSpinResult = NeonState & {
  spinId: string; gameId: typeof NEON_GAME_ID; symbolSetId: string
  wagerPoints: number; pointValueInCents: number; chargedWagerCents?: number
  reels: NeonSymbolId[][]; isFreeSpin: boolean; freeSpinsAwarded: number
  slotsCreditsBalance: number | null
  payout: { totalPoints: number; paylines: PaylinePayout[] }
  bananaBonusPoints: number
  neonArcade: {
    featureGamesEnabled: boolean; bonusSymbolCount: number
    activators: { kind: 'car' | 'disco-ball'; symbolId: NeonTriggerSymbol; positions: GridPosition[] }[]
  }
  neonDiscoCascade?: { initialReels: NeonDiscoSymbolId[][]; steps: NeonCascadeStep[]; totalPoints: number } | null
  neonNitroWild?: NeonNitroWild | null
}
export type NeonSpinRequest = {
  wagerPoints: number; useFreeSpin: boolean; state: NeonState
}

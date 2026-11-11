import type { ReactNode } from 'react'
import type { GridPosition, PaylinePayout } from '@fortuneforge/slot-client-core'

export const NEON_GAME_ID = 'neon-nights-v1'
export const NEON_DISCO_SYMBOL_SET_ID = 'neon-disco-symbols-v1'
export const discoMultipliers = [1, 2, 3, 5] as const
export type NeonDiscoMultiplier = typeof discoMultipliers[number]
export const neonBaseSymbolIds = ['2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BANANA', 'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW'] as const
export const neonDiscoSymbolIds = ['2', '4', '5', '7', 'DISCO_MARTINI', 'DISCO_BALL', 'DISCO_SEVEN'] as const
export const neonSymbolIds = [...neonBaseSymbolIds, 'DISCO_MARTINI', 'DISCO_BALL', 'DISCO_SEVEN'] as const
export type NeonSymbolId = typeof neonSymbolIds[number]
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
  paymentAlerts?: ReactNode
  onSpinStateChange?: (isSpinning: boolean) => void
}
export type NeonState = {
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  freeSpinFeatureMode: string | null
  neonDiscoSpinsRemaining: number
  neonDiscoMultiplier: NeonDiscoMultiplier
}
export type NeonCascadeStep = {
  multiplier: NeonDiscoMultiplier
  cleared: GridPosition[]
  reels: NeonDiscoSymbolId[][]
  dropRows: number[][]
  payout: { totalPoints: number; paylines: PaylinePayout[] }
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
}
export type NeonSpinRequest = {
  wagerPoints: number; useFreeSpin: boolean; state: NeonState
}

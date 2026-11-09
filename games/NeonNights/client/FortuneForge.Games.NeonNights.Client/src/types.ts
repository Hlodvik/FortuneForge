import type { ReactNode } from 'react'
import type { GridPosition, PaylinePayout } from '@fortuneforge/slot-client-core'

export const NEON_GAME_ID = 'neon-nights-v1'
export const neonSymbolIds = ['2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BANANA', 'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'] as const
export type NeonSymbolId = typeof neonSymbolIds[number]
export type NeonTriggerSymbol = '6' | 'POWER' | 'SEAL_RAND'
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
}
export type NeonCascadeStep = {
  cleared: GridPosition[]
  reels: NeonSymbolId[][]
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
    activators: { kind: 'car' | 'disco-ball' | 'arcade-cabinet'; symbolId: NeonTriggerSymbol; positions: GridPosition[] }[]
  }
  neonDiscoCascade?: { initialReels: NeonSymbolId[][]; steps: NeonCascadeStep[]; totalPoints: number } | null
}
export type NeonSpinRequest = {
  wagerPoints: number; useFreeSpin: boolean; state: NeonState
}

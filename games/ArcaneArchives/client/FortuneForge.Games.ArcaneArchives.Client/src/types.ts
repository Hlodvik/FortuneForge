import type { ReactNode } from 'react'
import type { GridPosition, LightningCollection, PaylinePayout } from '@fortuneforge/slot-client-core'
import type { PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import type { ArcaneWandCollection, SlotPotionState, SlotTomeState } from './arcaneFeatures.js'
import { ARCANE_ARCHIVES_GAME_ID } from './symbols.js'

export const ARCANE_GAME_ID = ARCANE_ARCHIVES_GAME_ID
export { arcaneWandPaylineId as ARCANE_WAND_PAYLINE_ID } from './arcaneFeatures.js'
export const ARCANE_LIGHTNING_PAYLINE_ID = 902

export type ArcaneAccount = { userId: string; role: string; balances: { slotsCredits: number } }
export type ArcaneAuthenticatedFetch = (path: string, init?: RequestInit) => Promise<Response>
export type ArcaneShellProps = { balance: number; onNavigate: () => void; children: ReactNode }
export type ArcaneArchivesGameProps = {
  account?: ArcaneAccount
  authenticatedFetch?: ArcaneAuthenticatedFetch
  renderShell?: (props: ArcaneShellProps) => ReactNode
  onSpinStateChange?: (isSpinning: boolean) => void
  paymentAlerts?: ReactNode
}

export type ArcaneState = {
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  freeSpinFeatureMode: string | null
  specialPointsBalance: number
  energyBalance: number
  arcaneTome: SlotTomeState | null
  arcanePotions: SlotPotionState | null
  slotsCreditsBalance?: number | null
  pendingOperation?: PreparedSlotSpin | null
}

export type ArcaneSpinTerms = { wagerPoints: number; useFreeSpin: boolean; useSpecialBoost: boolean }
export type ArcaneSpinResult = ArcaneState & {
  spinId: string
  gameId: typeof ARCANE_GAME_ID
  reelSetId: string
  symbolSetId: string
  paytableId: string
  pointValueInCents: number
  wagerPoints: number
  chargedWagerCents?: number
  reelStops: number[]
  reels: string[][]
  isFreeSpin: boolean
  freeSpinsAwarded: number
  specialPointsAwarded: number
  slotsCreditsBalance: number
  payout: { totalPoints: number; paylines: PaylinePayout[] }
  arcaneIngredientsAwarded?: Readonly<Record<string, number>> | null
  arcaneWandCollection?: ArcaneWandCollection | null
  lightningCollection?: LightningCollection | null
}

export type ArcaneRoundSummary = { totalRand: number; mode: string | null }
export type ArcaneSettledSpin = { key: number; reels: readonly (readonly string[])[] }
export type ArcaneWinningPosition = GridPosition

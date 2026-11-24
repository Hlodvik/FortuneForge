import type { ReactNode } from 'react'
import type { PaylinePayout } from '@fortuneforge/slot-client-core'
import type { PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import type { PharaohFeature, PharaohState } from './pharaoh'
import type { TombState } from './tomb'

export const PHARAOH_GAME_ID = 'cosmic-fortune-v1'
export type PharaohAuthenticatedFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
export type PharaohAccount = Readonly<{ userId: string; role: string; balances: Readonly<{ slotsCredits: number }> }>
export type PharaohShellProps = Readonly<{ balance: number; onNavigate: () => void; children: ReactNode }>
export type PharaohAscentGameProps = Readonly<{
  account?: PharaohAccount
  authenticatedFetch?: PharaohAuthenticatedFetch
  renderShell?: (props: PharaohShellProps) => ReactNode
  onSpinStateChange?: (isSpinning: boolean) => void
  paymentAlerts?: ReactNode
  demoMode?: boolean
}>
export type PharaohGameFeatureState = PharaohState & Readonly<{ tomb: TombState }>
export type PharaohSlotState = Readonly<{
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  freeSpinFeatureMode: string | null
  specialPointsBalance: number
  energyBalance: number
  sealCollections: PharaohState['collections']
  slotsCreditsBalance: number | null
  gameFeatureState: PharaohGameFeatureState
  featureActionRequired: boolean
  pendingOperation?: PreparedSlotSpin | null
}>
export type PharaohSpinResult = PharaohSlotState & Readonly<{
  spinId: string
  gameId: typeof PHARAOH_GAME_ID
  reelSetId: string
  symbolSetId: string
  paytableId: string
  wagerPoints: number
  pointValueInCents: number
  reels: readonly (readonly string[])[]
  reelStops: readonly number[]
  payout: Readonly<{ totalPoints: number; paylines: readonly PaylinePayout[] }>
  isFreeSpin: boolean
  chargedWagerCents: number
  spinFeature: PharaohFeature | null
  freeSpinsAwarded: number
}>

import type { ReactNode } from 'react'
import type { PreparedSlotSpin } from '@fortuneforge/slot-client-core/preparedSpin'
import ruleContract from './assets/contracts/crooked-road-v1.json' with { type: 'json' }
import type { RoadDestination, RoadFeatureEvent, RoadState } from './road.js'

export const CROOKED_ROAD_GAME_ID = 'crooked-road-v1'
export const CROOKED_ROAD_SYMBOL_SET_ID = 'crooked-road-v1-symbols'
export const CROOKED_ROAD_REEL_SET_ID = 'crooked-road-independent-reels-v1'
export const CROOKED_ROAD_RULES = ruleContract
export type CrookedRoadSymbolId = '2' | '3' | '4' | '5' | '6' | '7' | 'ACE' | 'FREE' | 'POWER'
export type CrookedRoadReels = readonly (readonly CrookedRoadSymbolId[])[]
export type CrookedRoadAuthenticatedFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
export type CrookedRoadAccount = Readonly<{ userId: string; role: string; balances: Readonly<{ slotsCredits: number }> }>
export type CrookedRoadPosition = Readonly<{ reel: number; row: number }>
export type CrookedRoadPayline = Readonly<{
  paylineId: number
  amountPoints: number
  matches: readonly Readonly<{
    amountPoints: number
    multiplier: number
    match: Readonly<{
      paylineId: number
      symbolId: CrookedRoadSymbolId
      matchLength: number
      positions: readonly CrookedRoadPosition[]
      wildPositions: readonly CrookedRoadPosition[]
    }>
  }>[]
}>
export type CrookedRoadState = Readonly<{
  gameFeatureState: RoadState
  featureActionRequired: boolean
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  freeSpinFeatureMode: RoadDestination | null
  slotsCreditsBalance: number | null
  pendingOperation?: PreparedSlotSpin | null
}>
export type CrookedRoadSpinResult = CrookedRoadState & Readonly<{
  spinId: string
  gameId: typeof CROOKED_ROAD_GAME_ID
  reelSetId: typeof CROOKED_ROAD_REEL_SET_ID
  symbolSetId: typeof CROOKED_ROAD_SYMBOL_SET_ID
  paytableId: string
  wagerPoints: number
  pointValueInCents: number
  reelStops: readonly number[]
  reels: CrookedRoadReels
  payout: Readonly<{ totalPoints: number; paylines: readonly CrookedRoadPayline[] }>
  isFreeSpin: boolean
  chargedWagerCents: number
  spinFeature: RoadFeatureEvent | null
}>
export type CrookedRoadShellProps = Readonly<{ balance: number; onNavigate: () => void; children: ReactNode }>
export type CrookedRoadGameProps = Readonly<{
  account?: CrookedRoadAccount
  authenticatedFetch?: CrookedRoadAuthenticatedFetch
  renderShell?: (props: CrookedRoadShellProps) => ReactNode
  onSpinStateChange?: (isSpinning: boolean) => void
  paymentAlerts?: ReactNode
  /** Compatibility at the mount boundary; the paid game rejects true. */
  demoMode?: boolean
}>

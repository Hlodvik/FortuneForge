export type HouseHoldemAction = 'check' | 'play-3x' | 'play-4x' | 'play-2x' | 'play-1x' | 'fold'
export type HouseHoldemPhase = 'preflop' | 'flop' | 'river' | 'settled'
export type HouseHoldemCard = Readonly<{ rank: string; suit: 'clubs' | 'diamonds' | 'hearts' | 'spades' }>
export type HouseHoldemStatus = Readonly<{
  available: boolean
  balance: number
  activeRoundId: string | null
  minimumAnte: number
  maximumAnte: number
  wagerIncrement: number
  maximumTrips: number
}>
export type HouseHoldemRound = Readonly<{
  roundId: string
  balance: number
  phase: HouseHoldemPhase
  ante: number
  blind: number
  trips: number
  play: number
  totalWager: number
  payout: number
  playerCards: readonly HouseHoldemCard[]
  dealerCards: readonly HouseHoldemCard[] | null
  board: readonly HouseHoldemCard[]
  playerHand: string | null
  dealerHand: string | null
  result: string | null
  allowedActions: readonly HouseHoldemAction[]
  canAdvance: boolean
  payoutBreakdown: Readonly<{ ante: number; blind: number; trips: number; play: number }> | null
}>

export const houseHoldemChipValues = [1, 2, 5, 10, 25] as const
export type HouseHoldemChipValue = typeof houseHoldemChipValues[number]
export type HouseHoldemBetSpot = 'ante' | 'trips'
export type HouseHoldemPlacement = Readonly<{ spot: HouseHoldemBetSpot; value: HouseHoldemChipValue }>
export type HouseHoldemDealPresentation = Readonly<{ roundId: string; step: number }>
export type HouseHoldemLoadState = Readonly<{ kind: 'loading' }> |
  Readonly<{ kind: 'ready'; status: HouseHoldemStatus }> |
  Readonly<{ kind: 'error' | 'disabled'; message: string }>

export function validHouseHoldemAnte(amount: number, status: HouseHoldemStatus): boolean {
  if (!Number.isFinite(amount) || amount < status.minimumAnte || amount > status.maximumAnte) return false
  const steps = (amount - status.minimumAnte) / status.wagerIncrement
  return Math.abs(steps - Math.round(steps)) < .000001
}

export function validHouseHoldemTrips(amount: number, status: HouseHoldemStatus): boolean {
  if (amount === 0) return true
  if (!Number.isFinite(amount) || amount < status.minimumAnte || amount > status.maximumTrips) return false
  const steps = (amount - status.minimumAnte) / status.wagerIncrement
  return Math.abs(steps - Math.round(steps)) < .000001
}

export function minimumHouseHoldemBankroll(ante: number, trips: number): number {
  // Ante + matching Blind are debited at deal; a final 1× Play is still mandatory unless the player folds.
  return Math.round((ante * 3 + trips) * 100) / 100
}

export function affordableHouseHoldemAction(round: HouseHoldemRound, action: HouseHoldemAction): boolean {
  if (!round.allowedActions.includes(action)) return false
  const multiplier = action.startsWith('play-') ? Number(action.slice(-2, -1)) : 0
  return round.balance * 100 + .000001 >= round.ante * multiplier * 100
}

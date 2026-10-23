export const dragonTigerSides = ['dragon', 'tiger', 'tie'] as const
export type DragonTigerSide = typeof dragonTigerSides[number]
export type DragonTigerRank = 'ace' | 'two' | 'three' | 'four' | 'five' | 'six' | 'seven' | 'eight' | 'nine' | 'ten' | 'jack' | 'queen' | 'king'
export type DragonTigerCard = Readonly<{ rank: DragonTigerRank; suit: 'clubs' | 'diamonds' | 'hearts' | 'spades' }>
export type DragonTigerStakes = Readonly<{ dragonStake: number; tigerStake: number; tieStake: number }>
export type DragonTigerStatus = Readonly<{
  available: boolean; minimumStake: number; maximumStake: number; maximumTotalStake: number; stakeIncrement: number; balance: number; mode: string
}>
export type DragonTigerBet = Readonly<{
  side: DragonTigerSide; stake: number; disposition: 'win' | 'loss' | 'half-loss' | 'not-placed'; profit: number; totalReturn: number
}>
export type DragonTigerRound = Readonly<{
  roundId: string; balance: number; phase: 'settled'; dragonCard: DragonTigerCard; tigerCard: DragonTigerCard;
  dragonValue: number; tigerValue: number; outcome: DragonTigerSide; bets: readonly DragonTigerBet[];
  totalStake: number; profit: number; totalReturn: number; shoeCardsUsed: number; shoeCardsRemaining: number
}>
export type DragonTigerRequestOptions = Readonly<{ signal?: AbortSignal; idempotencyKey: string }>
export interface DragonTigerGateway {
  getStatus(signal?: AbortSignal): Promise<DragonTigerStatus>
  createRound(stakes: DragonTigerStakes, options: DragonTigerRequestOptions): Promise<DragonTigerRound>
  getLatestRound?(signal?: AbortSignal): Promise<DragonTigerRound | null>
}
export class DragonTigerGatewayError extends Error {
  constructor(message: string, readonly code = 'dragon-tiger-request-failed', readonly status = 0) {
    super(message); this.name = 'DragonTigerGatewayError'
  }
}

export const andarBaharSides = ['andar', 'bahar'] as const
export type AndarBaharSide = typeof andarBaharSides[number]
export type AndarBaharRank = 'ace' | 'two' | 'three' | 'four' | 'five' | 'six' | 'seven' | 'eight' | 'nine' | 'ten' | 'jack' | 'queen' | 'king'
export type AndarBaharCard = Readonly<{ rank: AndarBaharRank; suit: 'clubs' | 'diamonds' | 'hearts' | 'spades' }>
export type AndarBaharDeal = Readonly<{ number: number; side: AndarBaharSide; card: AndarBaharCard }>
export type AndarBaharStatus = Readonly<{
  available: boolean
  minimumStake: number
  maximumStake: number
  stakeIncrement: number
  balance: number
  mode: 'development-practice-andar-first' | 'andar-first'
}>
/** A settled server result. Deals contain only the played prefix, ending at the first matching rank. */
export type AndarBaharRound = Readonly<{
  roundId: string
  balance: number
  phase: 'settled'
  joker: AndarBaharCard
  deals: readonly AndarBaharDeal[]
  winner: AndarBaharSide
  side: AndarBaharSide
  stake: number
  disposition: 'win' | 'loss'
  /** Net change after subtracting the wager. */
  profit: number
  /** Wager plus winnings, or zero after a loss. */
  totalReturn: number
}>
export type AndarBaharRequestOptions = Readonly<{ signal?: AbortSignal; idempotencyKey: string }>
export interface AndarBaharGateway {
  getStatus(signal?: AbortSignal): Promise<AndarBaharStatus>
  createRound(side: AndarBaharSide, stake: number, options: AndarBaharRequestOptions): Promise<AndarBaharRound>
  getLatestRound(signal?: AbortSignal): Promise<AndarBaharRound | null>
  getRound(roundId: string, signal?: AbortSignal): Promise<AndarBaharRound>
}
export class AndarBaharGatewayError extends Error {
  constructor(message: string, readonly code = 'andar-bahar-request-failed', readonly status = 0) {
    super(message)
    this.name = 'AndarBaharGatewayError'
  }
}

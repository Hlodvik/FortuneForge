export type BaccaratCardRank =
  | 'ace' | 'two' | 'three' | 'four' | 'five' | 'six' | 'seven'
  | 'eight' | 'nine' | 'ten' | 'jack' | 'queen' | 'king'

export type BaccaratCardSuit = 'clubs' | 'diamonds' | 'hearts' | 'spades'
export type BaccaratBetSide = 'player' | 'banker' | 'tie'
export type BaccaratRoundOutcome = 'player' | 'banker' | 'tie'
export type BaccaratBetDisposition = 'win' | 'loss' | 'push'
export type BaccaratPhase = 'settled'

export type BaccaratCard = Readonly<{ rank: BaccaratCardRank; suit: BaccaratCardSuit }>
export type BaccaratStatus = Readonly<{
  available: boolean
  minimumStake: number
  maximumStake: number
  stakeIncrement: number
  balance: number
  mode: string
  /** Missing on older servers; pair wagers must remain unavailable in that case. */
  supportsPairBets?: boolean
}>
export type BaccaratRound = Readonly<{
  roundId: string
  balance: number
  betSide: BaccaratBetSide
  stake: number
  phase: BaccaratPhase
  playerCards: readonly BaccaratCard[]
  bankerCards: readonly BaccaratCard[]
  playerTotal: number
  bankerTotal: number
  outcome: BaccaratRoundOutcome
  endedOnNatural: boolean
  disposition: BaccaratBetDisposition
  profit: number
  totalReturn: number
  playerPairStake?: number
  bankerPairStake?: number
  playerPairDisposition?: BaccaratBetDisposition | null
  bankerPairDisposition?: BaccaratBetDisposition | null
  playerPairProfit?: number
  bankerPairProfit?: number
  playerPairTotalReturn?: number
  bankerPairTotalReturn?: number
  totalStake?: number
  combinedProfit?: number
  combinedTotalReturn?: number
  shoeCardsUsed?: number
  shoeCardsRemaining?: number
}>
export type BaccaratRequestOptions = Readonly<{
  signal?: AbortSignal
  idempotencyKey?: string
  playerPairStake?: number
  bankerPairStake?: number
}>

export interface BaccaratGateway {
  getStatus(signal?: AbortSignal): Promise<BaccaratStatus>
  createRound(betSide: BaccaratBetSide, stake: number, options?: BaccaratRequestOptions): Promise<BaccaratRound>
}

export class BaccaratGatewayError extends Error {
  readonly code: string
  readonly status: number

  constructor(message: string, code = 'baccarat-request-failed', status = 0) {
    super(message)
    this.name = 'BaccaratGatewayError'
    this.code = code
    this.status = status
  }
}

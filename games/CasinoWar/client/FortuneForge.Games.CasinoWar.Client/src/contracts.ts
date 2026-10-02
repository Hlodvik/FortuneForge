export type CasinoWarCardRank =
  | 'ace' | 'two' | 'three' | 'four' | 'five' | 'six' | 'seven'
  | 'eight' | 'nine' | 'ten' | 'jack' | 'queen' | 'king'

export type CasinoWarCardSuit = 'clubs' | 'diamonds' | 'hearts' | 'spades'
export type CasinoWarPhase = 'awaiting-tie-decision' | 'completed'
export type CasinoWarDecision = 'surrender' | 'go-to-war'
export type CasinoWarSettlementDisposition = 'win' | 'loss' | 'surrender'
export type CasinoWarOpeningOutcome = 'player-win' | 'dealer-win' | 'tie-decision-required'
export type CasinoWarRoundOutcome =
  | 'player-opening-win' | 'dealer-opening-win' | 'player-surrendered'
  | 'player-war-win' | 'dealer-war-win' | 'player-war-tie-win'

export type CasinoWarCard = Readonly<{ rank: CasinoWarCardRank; suit: CasinoWarCardSuit }>

export type CasinoWarStatus = Readonly<{
  available: boolean
  minimumPrimaryStake: number
  maximumPrimaryStake: number
  stakeIncrement: number
  maximumTieStake: number
  balance: number
  mode: string
}>

export type CasinoWarPrimarySettlement = Readonly<{
  disposition: CasinoWarSettlementDisposition
  outcome: CasinoWarRoundOutcome
  totalWagered: number
  totalReturn: number
  profit: number
}>

export type CasinoWarTieSettlement = Readonly<{
  won: boolean
  disposition: CasinoWarSettlementDisposition
  outcome: CasinoWarOpeningOutcome
  stake: number
  totalReturn: number
  profit: number
}>

export type CasinoWarRound = Readonly<{
  roundId: string
  balance: number
  primaryStake: number
  tieStake: number
  phase: CasinoWarPhase
  playerOpeningCard: CasinoWarCard
  dealerOpeningCard: CasinoWarCard
  decision: CasinoWarDecision | null
  playerWarCard: CasinoWarCard | null
  dealerWarCard: CasinoWarCard | null
  primarySettlement: CasinoWarPrimarySettlement | null
  tieSettlement: CasinoWarTieSettlement | null
}>
export type CasinoWarRequestOptions = Readonly<{ signal?: AbortSignal; idempotencyKey?: string }>

export interface CasinoWarGateway {
  getStatus(signal?: AbortSignal): Promise<CasinoWarStatus>
  getRound(roundId: string, signal?: AbortSignal): Promise<CasinoWarRound>
  createRound(primaryStake: number, tieStake: number, options?: CasinoWarRequestOptions): Promise<CasinoWarRound>
  decide(roundId: string, decision: CasinoWarDecision, options?: CasinoWarRequestOptions): Promise<CasinoWarRound>
}

export class CasinoWarGatewayError extends Error {
  readonly code: string
  readonly status: number

  constructor(message: string, code = 'casino-war-request-failed', status = 0) {
    super(message)
    this.name = 'CasinoWarGatewayError'
    this.code = code
    this.status = status
  }
}

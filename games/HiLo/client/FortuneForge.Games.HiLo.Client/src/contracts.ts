export const hiLoRanks = ['ace', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'jack', 'queen', 'king'] as const
export type HiLoRank = typeof hiLoRanks[number]
export type HiLoSuit = 'clubs' | 'diamonds' | 'hearts' | 'spades'
export type HiLoCard = Readonly<{ rank: HiLoRank; suit: HiLoSuit }>
export type HiLoGuess = 'higher' | 'lower'
export type HiLoChoice = HiLoGuess | 'skip' | 'cashout'
export type HiLoRoundStatus = 'active' | 'lost' | 'cashedOut' | 'maximumReturn'
export type HiLoHistoryEntry = Readonly<{ card: HiLoCard; choice: 'start' | HiLoGuess | 'skip'; won: boolean | null }>
export type HiLoQuote = Readonly<{
  probability: number
  /** Gross cumulative round multiplier if this choice wins; includes returned stake. */
  multiplier: number
  projectedReturnCents: number
  available: boolean
}>
export type HiLoLimits = Readonly<{
  minWagerCents: number
  maxWagerCents: number
  maximumReturnCents: number
  maxMultiplier: number
  maxSkips: number
  maxStreak: number
  houseEdge: number
}>
export type HiLoRound = Readonly<{
  roundId: string
  revision: number
  status: HiLoRoundStatus
  wagerCents: number
  currentCard: HiLoCard
  history: readonly HiLoHistoryEntry[]
  currentReturnCents: number
  multiplier: number
  streak: number
  skips: number
  higher: HiLoQuote
  lower: HiLoQuote
  canCashOut: boolean
  canSkip: boolean
}>
export type HiLoSession = Readonly<{
  practice: boolean
  /** False during maintenance: existing rounds remain visible and can cash out. */
  enabled?: boolean
  walletBalanceCents: number
  limits: HiLoLimits
  round: HiLoRound | null
}>
export type HiLoRequestOptions = Readonly<{ operationId: string; signal?: AbortSignal }>
export type HiLoActionOptions = HiLoRequestOptions & Readonly<{ expectedRevision: number }>

export interface HiLoGateway {
  getSession(signal?: AbortSignal): Promise<HiLoSession>
  startRound(wagerCents: number, options: HiLoRequestOptions): Promise<HiLoSession>
  act(roundId: string, choice: HiLoChoice, options: HiLoActionOptions): Promise<HiLoSession>
}

export class HiLoGatewayError extends Error {
  constructor(message: string, readonly code = 'hi-lo-request-failed', readonly status = 0) {
    super(message)
    this.name = 'HiLoGatewayError'
  }
}

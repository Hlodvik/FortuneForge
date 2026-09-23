export type VideoPokerCardRank =
  | 'ace' | 'two' | 'three' | 'four' | 'five' | 'six' | 'seven'
  | 'eight' | 'nine' | 'ten' | 'jack' | 'queen' | 'king'

export type VideoPokerCardSuit = 'clubs' | 'diamonds' | 'hearts' | 'spades'
export type VideoPokerCardPosition = 0 | 1 | 2 | 3 | 4
export type VideoPokerHandCount = 1 | 3 | 5
export type VideoPokerPhase = 'awaiting-draw' | 'completed'
export type VideoPokerHandRank =
  | 'no-win' | 'pair' | 'two-pair' | 'three-of-a-kind' | 'straight'
  | 'flush' | 'full-house' | 'four-of-a-kind' | 'straight-flush' | 'royal-flush'

export type VideoPokerCard = Readonly<{ rank: VideoPokerCardRank; suit: VideoPokerCardSuit }>
export type VideoPokerStatus = Readonly<{
  available: boolean
  minimumCoinsWagered: number
  maximumCoinsWagered: number
  coinValue: number
  balance: number
  handCounts?: readonly VideoPokerHandCount[]
}>
export type VideoPokerRound = Readonly<{
  roundId: string
  balance: number
  coinsWagered: number
  handCount: VideoPokerHandCount
  wager: number
  phase: VideoPokerPhase
  initialCards: readonly VideoPokerCard[]
  heldPositions: readonly VideoPokerCardPosition[]
  finalCards: readonly VideoPokerCard[] | null
  handRank: VideoPokerHandRank | null
  payout: number | null
  finalHands: readonly (readonly VideoPokerCard[])[] | null
  handRanks: readonly VideoPokerHandRank[] | null
  handPayouts: readonly number[] | null
}>
export type VideoPokerRequestOptions = Readonly<{ signal?: AbortSignal; idempotencyKey?: string; handCount?: VideoPokerHandCount }>

export interface VideoPokerGateway {
  getStatus(signal?: AbortSignal): Promise<VideoPokerStatus>
  getRound(roundId: string, signal?: AbortSignal): Promise<VideoPokerRound>
  createRound(coinsWagered: number, options?: VideoPokerRequestOptions): Promise<VideoPokerRound>
  draw(roundId: string, heldPositions: readonly VideoPokerCardPosition[], options?: VideoPokerRequestOptions): Promise<VideoPokerRound>
}

export class VideoPokerGatewayError extends Error {
  readonly code: string
  readonly status: number

  constructor(message: string, code = 'video-poker-request-failed', status = 0) {
    super(message)
    this.name = 'VideoPokerGatewayError'
    this.code = code
    this.status = status
  }
}

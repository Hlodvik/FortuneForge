import type { CardRank, CardSuit } from '../shared/cards'

export const SOLITAIRE_PLAYER_COUNTS = [4, 6, 8] as const
export const SOLITAIRE_BUY_INS = [5, 10, 25] as const
export const SOLITAIRE_DRAW_COUNTS = [3, 1] as const

export type SolitairePlayerCount = (typeof SOLITAIRE_PLAYER_COUNTS)[number]
export type SolitaireBuyIn = (typeof SOLITAIRE_BUY_INS)[number]
export type SolitaireDrawCount = (typeof SOLITAIRE_DRAW_COUNTS)[number]
export type SolitairePlayerStatus =
  | 'open'
  | 'queued'
  | 'playing'
  | 'finished'
  | 'forfeited'
  | 'integrity-failed'
export type SolitairePileZone = 'waste' | 'foundation' | 'tableau'

export type SolitairePlayer = Readonly<{
  playerId: string
  displayName: string
  seat: number
  joinedAtUtc: string
  status: SolitairePlayerStatus
  isCurrentPlayer: boolean
  score?: number | null
  moves?: number | null
  elapsedSeconds?: number | null
}>

export type SolitaireFaceUpCard = Readonly<{
  id: string
  suit: CardSuit
  rank: CardRank
  isFaceUp: true
}>

export type SolitaireFaceDownCard = Readonly<{
  id?: null
  suit?: null
  rank?: null
  isFaceUp: false
}>

export type SolitaireCard = SolitaireFaceUpCard | SolitaireFaceDownCard

export type SolitaireGame = Readonly<{
  stock: readonly SolitaireCard[]
  waste: readonly SolitaireCard[]
  foundations: readonly (readonly SolitaireCard[])[]
  tableau: readonly (readonly SolitaireCard[])[]
  drawCount: number
  score: number
  moves: number
  message: string
}>

export type SolitaireIdleSession = Readonly<{ kind: 'idle' }>

export type SolitaireQueueSession = Readonly<{
  kind: 'queued'
  ticketId: string
  playerCount: SolitairePlayerCount
  buyInCredits: SolitaireBuyIn
  prizePoolCredits: number
  winnerPayoutCredits: number
  position: number
  joinedAtUtc: string
  players: readonly SolitairePlayer[]
}>

export type SolitaireMatchSession = Readonly<{
  kind: 'match'
  matchId: string
  playerCount: SolitairePlayerCount
  buyInCredits: SolitaireBuyIn
  prizePoolCredits: number
  winnerPayoutCredits: number
  startedAtUtc: string
  deadlineAtUtc: string
  version: number
  score: number
  moves: number
  remainingMilliseconds: number
  isPaused: boolean
  pauseRemainingMilliseconds: number
  canUndo: boolean
  integrityWarning?: SolitaireIntegrityWarning | null
  game: SolitaireGame
  players: readonly SolitairePlayer[]
}>

export type SolitaireIntegrityWarning = Readonly<{
  warningId: string
  reason: string
  purpose: string
  occurredAtUtc: string
  acknowledged: boolean
}>

export type SolitaireStanding = Readonly<{
  rank: number
  playerId: string
  displayName: string
  score: number
  moves: number
  elapsedSeconds: number
  status: 'finished' | 'forfeited' | 'integrity-failed'
  payoutCredits: number
  isCurrentPlayer: boolean
}>

export type SolitaireResultSession = Readonly<{
  kind: 'result'
  matchId: string
  playerCount: SolitairePlayerCount
  buyInCredits: SolitaireBuyIn
  prizePoolCredits: number
  winnerPayoutCredits: number
  platformFeeCredits: number
  startedAtUtc: string
  completedAtUtc: string
  standings: readonly SolitaireStanding[]
  claimStatus: 'unclaimed' | 'completed'
  canClaim: boolean
}>

export type SolitaireSession =
  | SolitaireIdleSession
  | SolitaireQueueSession
  | SolitaireMatchSession
  | SolitaireResultSession

export type SolitaireHistoryItem = Readonly<{
  matchId: string
  playerCount: SolitairePlayerCount
  buyInCredits: SolitaireBuyIn
  prizePoolCredits: number
  placement: number
  score: number
  elapsedSeconds: number
  payoutCredits: number
  netCredits: number
  completedAtUtc: string
  opponents: readonly string[]
}>

export type SolitaireMutationResponse = Readonly<{
  session: SolitaireSession
  balanceCredits: number
}>

export type SolitairePileReference = Readonly<{
  zone: SolitairePileZone
  index: number
}>

export type SolitaireCommand =
  | Readonly<{ type: 'draw' }>
  | Readonly<{ type: 'flip'; column: number }>
  | Readonly<{
      type: 'move'
      from: SolitairePileReference
      startIndex: number
      to: SolitairePileReference
    }>
  | Readonly<{ type: 'undo' }>
  | Readonly<{ type: 'pause' }>
  | Readonly<{ type: 'resume' }>
  | Readonly<{ type: 'submit' }>
  | Readonly<{ type: 'integrity-failure' }>
  | Readonly<{ type: 'acknowledge-warning' }>

export type SolitaireCommandRequest = SolitaireCommand & Readonly<{
  expectedVersion: number
}>

export type SolitaireAvailability =
  | Readonly<{ kind: 'loading' }>
  | Readonly<{ kind: 'ready'; session: SolitaireSession }>
  | Readonly<{ kind: 'disabled'; message: string }>
  | Readonly<{ kind: 'error'; message: string }>

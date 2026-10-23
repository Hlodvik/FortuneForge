export type HeartsPhase = 'passing' | 'playing' | 'complete'
export type HeartsPassDirection = 'left' | 'right' | 'across' | 'hold'
export type HeartsSeat = 'north' | 'east' | 'south' | 'west'
export type HeartsDifficulty = 'relaxed' | 'standard' | 'sharp'

export type HeartsCard = Readonly<{ code: string; rank: string; suit: string; label: string }>
export type HeartsPlayer = Readonly<{ seat: HeartsSeat; displayName: string; handCount: number; roundScore: number; matchScore: number; hasPassed: boolean }>
export type HeartsTrickPlay = Readonly<{ seat: HeartsSeat; card: HeartsCard }>
export type HeartsTrick = Readonly<{ leader: HeartsSeat; plays: readonly HeartsTrickPlay[] }>
export type HeartsRecentTrick = Readonly<{ number: number; leader: HeartsSeat; winner: HeartsSeat; points: number; plays: readonly HeartsTrickPlay[] }>
export type HeartsCompletedTrick = Readonly<{ number: number; leader: HeartsSeat; winner: HeartsSeat; points: number }>
export type HeartsScore = Readonly<{ north: number; east: number; south: number; west: number }>

export type HeartsStatus = Readonly<{ available: boolean; defaultTargetScore: number; mode: string }>
export type HeartsMatch = Readonly<{
  matchId: string
  targetScore: number
  roundNumber: number
  phase: HeartsPhase
  passDirection: HeartsPassDirection
  turn: HeartsSeat
  humanSeat: HeartsSeat
  yourTurn: boolean
  opponentsThinking: boolean
  heartsBroken: boolean
  submittedPasses: readonly HeartsSeat[]
  players: readonly HeartsPlayer[]
  hand: readonly HeartsCard[]
  legalCards: readonly HeartsCard[]
  currentTrick: HeartsTrick
  recentTrick: HeartsRecentTrick | null
  completedTricks: readonly HeartsCompletedTrick[]
  score: HeartsScore
  roundScore: HeartsScore
  difficulty: HeartsDifficulty
  winner: HeartsSeat | null
  message: string
  isTrial?: boolean
}>

export type HeartsOnboardingState = Readonly<{ shouldAskPlayedBefore: boolean }>
export interface HeartsOnboardingGateway {
  getState(signal?: AbortSignal): Promise<HeartsOnboardingState>
  confirmExperience(signal?: AbortSignal): Promise<HeartsOnboardingState>
}

export interface HeartsGateway {
  paid?: HeartsPaidGateway
  onboarding?: HeartsOnboardingGateway
  getStatus(signal?: AbortSignal): Promise<HeartsStatus>
  startTrial?(signal?: AbortSignal): Promise<HeartsMatch>
  cancelTrial?(matchId: string, signal?: AbortSignal): Promise<HeartsOnboardingState>
  startMatch(options: { targetScore: number; difficulty?: HeartsDifficulty; seed?: number }, signal?: AbortSignal): Promise<HeartsMatch>
  leaveMatch?(matchId: string, signal?: AbortSignal): Promise<void>
  pass(matchId: string, cards: readonly string[], signal?: AbortSignal): Promise<HeartsMatch>
  playCard(matchId: string, card: string, signal?: AbortSignal): Promise<HeartsMatch>
  advance(matchId: string, signal?: AbortSignal): Promise<HeartsMatch>
  nextRound(matchId: string, seed?: number, signal?: AbortSignal): Promise<HeartsMatch>
}

export type HeartsPaidPlayer = Readonly<{ seat: HeartsSeat; displayName: string; isCurrentPlayer: boolean; hasLeft: boolean; entryCredits: number; payoutCredits: number }>
export type HeartsPaidTable = Readonly<{ match: HeartsMatch; entryCredits: number; potCredits: number; payoutCredits: number; winnerSeats: readonly HeartsSeat[]; players: readonly HeartsPaidPlayer[]; actionDeadlineAtUtc: string | null; completedAtUtc: string | null }>
export type HeartsPaidQueue = Readonly<{ matchId: string; entryCredits: number; potCredits: number; joinedAtUtc: string; players: readonly Readonly<{ seat: HeartsSeat; displayName: string; isCurrentPlayer: boolean }>[]; message: string; canPlayHouse?: boolean }>
export type HeartsPaidSession = Readonly<{ kind: 'idle' | 'queue' | 'match' | 'result'; version: number; balanceCredits: number; entryOptions: readonly number[]; queue: HeartsPaidQueue | null; table: HeartsPaidTable | null; notice?: string | null }>
export type HeartsPaidRequest = Readonly<{ idempotencyKey: string; expectedVersion?: number; signal?: AbortSignal }>
export interface HeartsPaidGateway {
  getSession(signal?: AbortSignal): Promise<HeartsPaidSession>
  join(entryCredits: number, options: HeartsPaidRequest): Promise<HeartsPaidSession>
  playHouse?(matchId: string, options: HeartsPaidRequest): Promise<HeartsPaidSession>
  getMatch(matchId: string, signal?: AbortSignal): Promise<HeartsPaidSession>
  pass(matchId: string, cards: readonly string[], options: HeartsPaidRequest): Promise<HeartsPaidSession>
  play(matchId: string, card: string, options: HeartsPaidRequest): Promise<HeartsPaidSession>
  advance(matchId: string, options: HeartsPaidRequest): Promise<HeartsPaidSession>
  nextRound(matchId: string, options: HeartsPaidRequest): Promise<HeartsPaidSession>
  leave(matchId: string, options: HeartsPaidRequest): Promise<HeartsPaidSession>
  getHistory(signal?: AbortSignal): Promise<readonly HeartsPaidTable[]>
}

export class HeartsGatewayError extends Error {
  readonly code: string
  readonly status: number
  constructor(message: string, code = 'hearts-request-failed', status = 0) { super(message); this.name = 'HeartsGatewayError'; this.code = code; this.status = status }
}

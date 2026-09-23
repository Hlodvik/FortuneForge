export type HeartsPhase = 'passing' | 'playing' | 'complete'
export type HeartsPassDirection = 'left' | 'right' | 'across' | 'hold'
export type HeartsSeat = 'north' | 'east' | 'south' | 'west'
export type HeartsDifficulty = 'relaxed' | 'standard' | 'sharp'

export type HeartsCard = Readonly<{ code: string; rank: string; suit: string; label: string }>
export type HeartsPlayer = Readonly<{ seat: HeartsSeat; handCount: number; roundScore: number; matchScore: number; hasPassed: boolean }>
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
  botsThinking: boolean
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
}>

export interface HeartsGateway {
  getStatus(signal?: AbortSignal): Promise<HeartsStatus>
  startMatch(options: { targetScore: number; difficulty?: HeartsDifficulty; seed?: number }, signal?: AbortSignal): Promise<HeartsMatch>
  pass(matchId: string, cards: readonly string[], signal?: AbortSignal): Promise<HeartsMatch>
  playCard(matchId: string, card: string, signal?: AbortSignal): Promise<HeartsMatch>
  advance(matchId: string, signal?: AbortSignal): Promise<HeartsMatch>
  nextRound(matchId: string, seed?: number, signal?: AbortSignal): Promise<HeartsMatch>
}

export class HeartsGatewayError extends Error {
  readonly code: string
  readonly status: number
  constructor(message: string, code = 'hearts-request-failed', status = 0) { super(message); this.name = 'HeartsGatewayError'; this.code = code; this.status = status }
}

export type LiarsDicePhase = 'bidding' | 'resolved'
export type LiarsDiceBid = Readonly<{ quantity: number; face: number }>
export type LiarsDicePlayer = Readonly<{ id: string; displayName: string; diceCount: number; active: boolean; isHuman: boolean }>
export type LiarsDiceOutcome = Readonly<{ challengerId: string; bidderId: string; loserId: string; quantity: number; face: number; matchingDice: number }>
export type LiarsDiceStatus = Readonly<{ available: boolean; startingDicePerPlayer: number; mode: string }>
export type LiarsDiceMatch = Readonly<{ matchId: string; phase: LiarsDicePhase; roundNumber: number; currentPlayerId: string; currentBid: LiarsDiceBid | null; currentBidderId: string | null; totalDice: number; hand: readonly number[]; players: readonly LiarsDicePlayer[]; outcome: LiarsDiceOutcome | null; winner: string | null; message: string }>

export interface LiarsDiceGateway {
  getStatus(signal?: AbortSignal): Promise<LiarsDiceStatus>
  startMatch(options: { dicePerPlayer: number; seed?: number }, signal?: AbortSignal): Promise<LiarsDiceMatch>
  bid(matchId: string, bid: { quantity: number; face: number }, signal?: AbortSignal): Promise<LiarsDiceMatch>
  challenge(matchId: string, signal?: AbortSignal): Promise<LiarsDiceMatch>
  advance(matchId: string, signal?: AbortSignal): Promise<LiarsDiceMatch>
  nextRound(matchId: string, seed?: number, signal?: AbortSignal): Promise<LiarsDiceMatch>
}

export class LiarsDiceGatewayError extends Error {
  readonly code: string
  readonly status: number
  constructor(message: string, code = 'liars-dice-request-failed', status = 0) { super(message); this.name = 'LiarsDiceGatewayError'; this.code = code; this.status = status }
}

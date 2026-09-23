import { LiarsDiceGatewayError, type LiarsDiceGateway, type LiarsDiceMatch, type LiarsDiceStatus } from './contracts'

export class HttpLiarsDiceGateway implements LiarsDiceGateway {
  readonly basePath: string
  constructor(basePath = '/api/games/liars-dice') { this.basePath = basePath.replace(/\/$/, '') }
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  startMatch(options: { dicePerPlayer: number; seed?: number }, signal?: AbortSignal) { return this.request('/matches', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(options), signal }, isMatch) }
  bid(matchId: string, bid: { quantity: number; face: number }, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/bid`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(bid), signal }, isMatch) }
  challenge(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/challenge`, { method: 'POST', signal }, isMatch) }
  advance(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/advance`, { method: 'POST', signal }, isMatch) }
  nextRound(matchId: string, seed?: number, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/next-round`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ seed }), signal }, isMatch) }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> { const response = await fetch(`${this.basePath}${path}`, init); const value: unknown = await response.json().catch(() => null); if (!response.ok) { const error = isError(value) ? value : null; throw new LiarsDiceGatewayError(error?.message ?? `The Liar's Dice request failed (${response.status}).`, error?.code, response.status) } if (!validate(value)) throw new LiarsDiceGatewayError("The Liar's Dice service returned an invalid response."); return value }
}

const jsonHeaders = { 'content-type': 'application/json' }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is LiarsDiceStatus { return isRecord(value) && typeof value.available === 'boolean' && Number.isInteger(value.startingDicePerPlayer) && typeof value.mode === 'string' }
function isMatch(value: unknown): value is LiarsDiceMatch { return isRecord(value) && typeof value.matchId === 'string' && isPhase(value.phase) && Number.isInteger(value.roundNumber) && typeof value.currentPlayerId === 'string' && (value.currentBid === null || isBid(value.currentBid)) && (value.currentBidderId === null || typeof value.currentBidderId === 'string') && Number.isInteger(value.totalDice) && Array.isArray(value.hand) && value.hand.every(face => Number.isInteger(face) && face >= 1 && face <= 6) && Array.isArray(value.players) && Array.isArray(value.outcome) === false && (value.outcome === null || isOutcome(value.outcome)) && (value.winner === null || typeof value.winner === 'string') && typeof value.message === 'string' }
function isBid(value: unknown): boolean { return isRecord(value) && isPositiveInteger(value.quantity) && isFace(value.face) }
function isOutcome(value: unknown): boolean { return isRecord(value) && typeof value.challengerId === 'string' && typeof value.bidderId === 'string' && typeof value.loserId === 'string' && Number.isInteger(value.quantity) && Number.isInteger(value.face) && Number.isInteger(value.matchingDice) }
function isPhase(value: unknown): value is LiarsDiceMatch['phase'] { return value === 'bidding' || value === 'resolved' }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }
function isPositiveInteger(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value > 0 }
function isFace(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 6 }

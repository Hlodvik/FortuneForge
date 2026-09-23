import { HeartsGatewayError, type HeartsDifficulty, type HeartsGateway, type HeartsMatch, type HeartsSeat, type HeartsStatus } from './contracts'

export class HttpHeartsGateway implements HeartsGateway {
  readonly basePath: string
  constructor(basePath = '/api/games/hearts') { this.basePath = basePath.replace(/\/$/, '') }
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  startMatch(options: { targetScore: number; difficulty?: HeartsDifficulty; seed?: number }, signal?: AbortSignal) { return this.request('/matches', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(options), signal }, isMatch) }
  pass(matchId: string, cards: readonly string[], signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/pass`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ cards }), signal }, isMatch) }
  playCard(matchId: string, card: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/card`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ card }), signal }, isMatch) }
  advance(matchId: string, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/advance`, { method: 'POST', signal }, isMatch) }
  nextRound(matchId: string, seed?: number, signal?: AbortSignal) { return this.request(`/matches/${encodeURIComponent(matchId)}/next-round`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ seed }), signal }, isMatch) }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await fetch(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) { const error = isError(value) ? value : null; throw new HeartsGatewayError(error?.message ?? `The Hearts table request failed (${response.status}).`, error?.code, response.status) }
    if (!validate(value)) throw new HeartsGatewayError('The Hearts service returned an invalid response.')
    return value
  }
}

const jsonHeaders = { 'content-type': 'application/json' }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is HeartsStatus { return isRecord(value) && typeof value.available === 'boolean' && Number.isInteger(value.defaultTargetScore) && typeof value.mode === 'string' }
function isMatch(value: unknown): value is HeartsMatch { return isRecord(value) && typeof value.matchId === 'string' && Number.isInteger(value.targetScore) && Number.isInteger(value.roundNumber) && isPhase(value.phase) && isDirection(value.passDirection) && isSeat(value.turn) && isSeat(value.humanSeat) && typeof value.yourTurn === 'boolean' && typeof value.botsThinking === 'boolean' && typeof value.heartsBroken === 'boolean' && Array.isArray(value.players) && Array.isArray(value.hand) && value.hand.every(isCard) && Array.isArray(value.legalCards) && value.legalCards.every(isCard) && isTrick(value.currentTrick) && (value.recentTrick === null || isRecentTrick(value.recentTrick)) && Array.isArray(value.completedTricks) && isScore(value.score) && isScore(value.roundScore) && isDifficulty(value.difficulty) && (value.winner === null || isSeat(value.winner)) && typeof value.message === 'string' }
function isCard(value: unknown): boolean { return isRecord(value) && typeof value.code === 'string' && typeof value.label === 'string' && typeof value.suit === 'string' }
function isTrick(value: unknown): boolean { return isRecord(value) && isSeat(value.leader) && Array.isArray(value.plays) }
function isRecentTrick(value: unknown): boolean { return isRecord(value) && Number.isInteger(value.number) && isSeat(value.leader) && isSeat(value.winner) && Number.isInteger(value.points) && Array.isArray(value.plays) }
function isScore(value: unknown): boolean { return isRecord(value) && typeof value.north === 'number' && typeof value.east === 'number' && typeof value.south === 'number' && typeof value.west === 'number' }
function isPhase(value: unknown): value is HeartsMatch['phase'] { return value === 'passing' || value === 'playing' || value === 'complete' }
function isDirection(value: unknown): boolean { return value === 'left' || value === 'right' || value === 'across' || value === 'hold' }
function isDifficulty(value: unknown): value is HeartsDifficulty { return value === 'relaxed' || value === 'standard' || value === 'sharp' }
function isSeat(value: unknown): value is HeartsSeat { return value === 'north' || value === 'east' || value === 'south' || value === 'west' }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

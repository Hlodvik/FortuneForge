import { RouletteGatewayError, type RouletteBet, type RouletteBetKind, type RouletteGateway, type RouletteRound, type RouletteStatus } from './contracts'

export class HttpRouletteGateway implements RouletteGateway {
  readonly basePath: string
  constructor(basePath = '/api/games/roulette', private readonly fetcher: typeof fetch = fetch) { this.basePath = basePath.replace(/\/$/, '') }
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  getRound(roundId: string, signal?: AbortSignal) { return this.request(`/rounds/${encodeURIComponent(roundId)}`, { signal }, isRound, roundId) }
  openRound(signal?: AbortSignal) { return this.request('/rounds', { method: 'POST', signal }, isRound) }
  placeBet(roundId: string, bet: Omit<RouletteBet, 'betIndex' | 'playerId'>, signal?: AbortSignal) { return this.request(`/rounds/${encodeURIComponent(roundId)}/bets`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(bet), signal }, isRound, roundId) }
  removeBet(roundId: string, betIndex: number, signal?: AbortSignal) { return this.request(`/rounds/${encodeURIComponent(roundId)}/bets/${betIndex}`, { method: 'DELETE', signal }, isRound, roundId) }
  clearBets(roundId: string, signal?: AbortSignal) { return this.request(`/rounds/${encodeURIComponent(roundId)}/bets`, { method: 'DELETE', signal }, isRound, roundId) }
  spin(roundId: string, signal?: AbortSignal) { return this.request(`/rounds/${encodeURIComponent(roundId)}/spin`, { method: 'POST', signal }, isRound, roundId) }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T, expectedRoundId?: string): Promise<T> { const response = await this.fetcher(`${this.basePath}${path}`, init); const value: unknown = await response.json().catch(() => null); if (!response.ok) { const error = isError(value) ? value : null; throw new RouletteGatewayError(error?.message ?? `The Roulette request failed (${response.status}).`, error?.code, response.status) } if (!validate(value) || (expectedRoundId && (!isRecord(value) || value.roundId !== expectedRoundId))) throw new RouletteGatewayError('The Roulette service returned an invalid response.'); return value }
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is RouletteStatus { return isRecord(value) && typeof value.available === 'boolean' && positive(value.minimumStake) && positive(value.maximumStake) && value.maximumStake >= value.minimumStake && positive(value.stakeIncrement) && nonnegative(value.startingBalance) && typeof value.mode === 'string' }
function isRound(value: unknown): value is RouletteRound { return isRecord(value) && typeof value.roundId === 'string' && nonnegative(value.balance) && (value.phase === 'open' || value.phase === 'settled') && Array.isArray(value.bets) && value.bets.every(isBet) && (value.winningPocket === null || isPocket(value.winningPocket)) && Array.isArray(value.settlements) && value.settlements.every(isSettlement) && value.bets.every((bet, index) => bet.betIndex === index) && (value.phase === 'open' ? value.winningPocket === null && value.settlements.length === 0 : isPocket(value.winningPocket) && value.bets.length > 0 && value.settlements.length === value.bets.length) }
function isBet(value: unknown): value is RouletteBet { return isRecord(value) && Number.isInteger(value.betIndex) && Number(value.betIndex) >= 0 && typeof value.playerId === 'string' && isKind(value.kind) && positive(value.stake) && (value.number === null || isPocket(value.number)) && Array.isArray(value.numbers) && value.numbers.every(isPocket) }
function isSettlement(value: unknown): boolean { return isRecord(value) && typeof value.playerId === 'string' && isKind(value.kind) && typeof value.stake === 'number' && typeof value.won === 'boolean' && nonnegative(value.totalReturn) }
function isKind(value: unknown): value is RouletteBetKind { return value === 'straight' || value === 'split' || value === 'street' || value === 'corner' || value === 'six-line' || value === 'column' || value === 'dozen' || value === 'red' || value === 'black' || value === 'even' || value === 'odd' || value === 'low' || value === 'high' }
function isPocket(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 36 }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

function nonnegative(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function positive(value: unknown): value is number { return nonnegative(value) && value > 0 }

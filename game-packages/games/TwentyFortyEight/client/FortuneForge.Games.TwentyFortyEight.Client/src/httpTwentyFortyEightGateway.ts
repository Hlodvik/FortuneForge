import type { TwentyFortyEightDirection, TwentyFortyEightEvent, TwentyFortyEightGameState, TwentyFortyEightGateway, TwentyFortyEightPhase, TwentyFortyEightStatus } from './contracts'

export type TwentyFortyEightFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>

export class TwentyFortyEightGatewayError extends Error {
  readonly code?: string
  readonly status?: number

  constructor(message: string, code?: string, status?: number) {
    super(message)
    this.name = 'TwentyFortyEightGatewayError'
    this.code = code
    this.status = status
  }
}

export class HttpTwentyFortyEightGateway implements TwentyFortyEightGateway {
  constructor(
    private readonly basePath = '/api/games/2048',
    private readonly fetcher: TwentyFortyEightFetch = fetch,
  ) {}

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }

  startGame(seed?: number, signal?: AbortSignal) {
    return this.request('/games', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  move(gameId: string, direction: TwentyFortyEightDirection, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/move`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ direction }), signal }, isGame)
  }

  undo(gameId: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/undo`, { method: 'POST', signal }, isGame)
  }

  continueGame(gameId: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/continue`, { method: 'POST', signal }, isGame)
  }

  reset(gameId: string, seed?: number, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/reset`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await this.fetcher(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new TwentyFortyEightGatewayError(error?.message ?? `The 2048 request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new TwentyFortyEightGatewayError('The 2048 service returned an invalid response.')
    return value
  }
}

const jsonHeaders = { 'content-type': 'application/json' }

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is TwentyFortyEightStatus { return isRecord(value) && typeof value.available === 'boolean' && Number.isInteger(value.size) && Number.isInteger(value.targetTile) && typeof value.mode === 'string' }
function isGame(value: unknown): value is TwentyFortyEightGameState {
  return isRecord(value) && typeof value.gameId === 'string' && typeof value.size === 'number' && Number.isInteger(value.size) && Array.isArray(value.tiles) && value.tiles.length === value.size * value.size && value.tiles.every(tile => Number.isInteger(tile) && tile >= 0) && Number.isInteger(value.score) && Number.isInteger(value.moves) && Number.isInteger(value.highestTile) && isPhase(value.phase) && typeof value.canUndo === 'boolean' && isEvent(value.lastEvent) && Number.isInteger(value.scoreGained) && typeof value.message === 'string'
}
function isPhase(value: unknown): value is TwentyFortyEightPhase { return value === 'playing' || value === 'won' || value === 'lost' }
function isEvent(value: unknown): value is TwentyFortyEightEvent { return value === 'started' || value === 'moved' || value === 'no-move' || value === 'won' || value === 'lost' || value === 'undone' || value === 'continued' }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

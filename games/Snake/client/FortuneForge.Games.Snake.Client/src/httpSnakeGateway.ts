import type { GameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import type { SnakeDirection, SnakeEvent, SnakeGameState, SnakeGateway, SnakeOptions, SnakePhase, SnakePoint, SnakeStatus } from './contracts'

export class SnakeGatewayError extends Error {
  readonly code?: string
  readonly status?: number

  constructor(message: string, code?: string, status?: number) {
    super(message)
    this.name = 'SnakeGatewayError'
    this.code = code
    this.status = status
  }
}

export class HttpSnakeGateway implements SnakeGateway {
  constructor(private readonly basePath = '/api/games/snake', private readonly fetcher: typeof fetch = (...args) => fetch(...args), private readonly recovery?: GameCommandRecovery, private readonly expectedAccountId?: string) {}

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  getGame(gameId: string, signal?: AbortSignal) { return this.request(`/games/${encodeURIComponent(gameId)}`, { signal }, isGame) }

  startGame(seed?: number, signal?: AbortSignal, options?: SnakeOptions) {
    return this.request('/games', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ ...(seed === undefined ? {} : { seed }), ...options }), signal }, isGame)
  }

  turn(gameId: string, direction: SnakeDirection, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/turn`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ direction }), signal }, isGame)
  }

  tick(gameId: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/tick`, { method: 'POST', signal }, isGame)
  }

  reset(gameId: string, seed?: number, signal?: AbortSignal, options?: SnakeOptions) {
    return this.request(`/games/${encodeURIComponent(gameId)}/reset`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ ...(seed === undefined ? {} : { seed }), ...options }), signal }, isGame)
  }

  async recoverGame(signal?: AbortSignal) {
    const saved = this.recovery?.read()
    if (saved?.pending) {
      const pending = saved.pending
      if (!pending.path.startsWith(this.basePath + '/')) throw new SnakeGatewayError('The saved command belongs to a different game service.')
      const recovered = await this.request(pending.path.slice(this.basePath.length),
        { method: 'POST', headers: jsonHeaders, body: pending.body, signal }, isGame)
      return this.getGame(recovered.gameId, signal)
    }
    return saved?.activeBoardId ? this.getGame(saved.activeBoardId, signal) : this.startGame(undefined, signal)
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const target = this.basePath + path
    const command = init.method === 'POST' ? this.recovery?.prepare(target, String(init.body ?? '')) : undefined
    if (command) {
      const headers = new Headers(init.headers)
      headers.set('Idempotency-Key', command.idempotencyKey)
      init = { ...init, headers, body: command.body }
    }
    if (this.expectedAccountId) {
      const headers = new Headers(init.headers)
      headers.set('X-FortuneForge-Expected-Account', this.expectedAccountId)
      init = { ...init, headers }
    }
    const response = await this.fetcher(target, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      if (command && response.status < 500 && ![401, 403, 408, 425, 429].includes(response.status)) this.recovery?.complete(command)
      const error = isError(value) ? value : null
      throw new SnakeGatewayError(error?.message ?? `The Snake request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new SnakeGatewayError('The Snake service returned an invalid response.')
    if (command && isGame(value)) this.recovery?.complete(command, value.gameId)
    return value
  }
}

const jsonHeaders = { 'content-type': 'application/json' }

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is SnakeStatus { return isRecord(value) && typeof value.available === 'boolean' && isInteger(value.width) && isInteger(value.height) && isInteger(value.foodScore) && isInteger(value.tickMilliseconds) && typeof value.mode === 'string' }
function isGame(value: unknown): value is SnakeGameState {
  return isRecord(value) && typeof value.gameId === 'string' && isInteger(value.width) && isInteger(value.height) && Array.isArray(value.body) && isInteger(value.length) && value.body.length === value.length && value.body.every(isPoint) && (value.food === null || isPoint(value.food)) && isDirection(value.direction) && isInteger(value.score) && isInteger(value.bestScore) && isInteger(value.moves) && isPhase(value.phase) && isEvent(value.lastEvent) && isInteger(value.scoreGained) && typeof value.message === 'string'
}
function isPoint(value: unknown): value is SnakePoint { return isRecord(value) && isInteger(value.x) && isInteger(value.y) }
function isDirection(value: unknown): value is SnakeDirection { return value === 'up' || value === 'right' || value === 'down' || value === 'left' }
function isPhase(value: unknown): value is SnakePhase { return value === 'playing' || value === 'won' || value === 'lost' }
function isEvent(value: unknown): value is SnakeEvent { return value === 'started' || value === 'turned' || value === 'moved' || value === 'ate-food' || value === 'no-op' || value === 'won' || value === 'lost' }
function isInteger(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

import type { SnakeDirection, SnakeEvent, SnakeGameState, SnakeGateway, SnakePhase, SnakePoint, SnakeStatus } from './contracts'

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
  constructor(private readonly basePath = '/api/games/snake') {}

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }

  startGame(seed?: number, signal?: AbortSignal) {
    return this.request('/games', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  turn(gameId: string, direction: SnakeDirection, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/turn`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ direction }), signal }, isGame)
  }

  tick(gameId: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/tick`, { method: 'POST', signal }, isGame)
  }

  reset(gameId: string, seed?: number, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/reset`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await fetch(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new SnakeGatewayError(error?.message ?? `The Snake request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new SnakeGatewayError('The Snake service returned an invalid response.')
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

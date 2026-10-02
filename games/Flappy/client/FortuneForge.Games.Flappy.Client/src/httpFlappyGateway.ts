import type { FlappyGameState, FlappyGateway, FlappyPhase, FlappyStatus } from './contracts'

export class HttpFlappyGateway implements FlappyGateway {
  constructor(private readonly basePath = '/api/games/flappy') {}
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  startGame(seed?: number, signal?: AbortSignal) { return this.request('/games', jsonPost(seed === undefined ? {} : { seed }, signal), isGame) }
  step(gameId: string, flap: boolean, signal?: AbortSignal) { return this.request(`/games/${encodeURIComponent(gameId)}/step`, jsonPost({ flap }, signal), isGame) }
  reset(gameId: string, seed?: number, signal?: AbortSignal) { return this.request(`/games/${encodeURIComponent(gameId)}/reset`, jsonPost(seed === undefined ? {} : { seed }, signal), isGame) }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await fetch(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) throw new Error(`The Flappy request failed (${response.status}).`)
    if (!validate(value)) throw new Error('The Flappy service returned an invalid response.')
    return value
  }
}

function jsonPost(body: unknown, signal?: AbortSignal): RequestInit { return { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body), signal } }
function record(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function finite(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) }
function phase(value: unknown): value is FlappyPhase { return value === 'playing' || value === 'obstacle-collision' || value === 'ground-collision' || value === 'ceiling-collision' }
function isStatus(value: unknown): value is FlappyStatus { return record(value) && typeof value.available === 'boolean' && value.tickMilliseconds === 20 && typeof value.mode === 'string' }
function isGame(value: unknown): value is FlappyGameState { return record(value) && typeof value.gameId === 'string' && finite(value.width) && finite(value.height) && finite(value.birdX) && finite(value.birdY) && finite(value.birdRadius) && finite(value.obstacleWidth) && finite(value.score) && finite(value.bestScore) && finite(value.level) && phase(value.phase) && Array.isArray(value.obstacles) && value.obstacles.every(obstacle => record(obstacle) && finite(obstacle.id) && finite(obstacle.x) && finite(obstacle.gapTop) && finite(obstacle.gapBottom)) }

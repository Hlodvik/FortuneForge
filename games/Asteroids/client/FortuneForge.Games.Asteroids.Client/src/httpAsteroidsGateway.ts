import type { Asteroid, AsteroidKind, AsteroidSize, AsteroidsAction, AsteroidsEvent, AsteroidsGameState, AsteroidsGateway, AsteroidsLeaderboard, AsteroidsPhase, AsteroidsPowerUpType, AsteroidsShip, AsteroidsStatus } from './contracts'

export class AsteroidsGatewayError extends Error {
  readonly code?: string
  readonly status?: number

  constructor(message: string, code?: string, status?: number) {
    super(message)
    this.name = 'AsteroidsGatewayError'
    this.code = code
    this.status = status
  }
}

export class HttpAsteroidsGateway implements AsteroidsGateway {
  constructor(private readonly basePath = '/api/games/asteroids') {}

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }

  startGame(seed?: number, signal?: AbortSignal) {
    return this.request('/games', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  action(gameId: string, action: AsteroidsAction, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/action`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ action }), signal }, isGame)
  }

  reset(gameId: string, seed?: number, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/reset`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  getLeaderboard(signal?: AbortSignal) { return this.request('/leaderboard', { signal }, isLeaderboard) }

  submitScore(gameId: string, playerName?: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/submit`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ playerName }), signal }, isLeaderboard)
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await fetch(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new AsteroidsGatewayError(error?.message ?? `The Asteroids request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new AsteroidsGatewayError('The Asteroids service returned an invalid response.')
    return value
  }
}

const jsonHeaders = { 'content-type': 'application/json' }

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is AsteroidsStatus { return isRecord(value) && typeof value.available === 'boolean' && isInteger(value.width) && isInteger(value.height) && isInteger(value.tickMilliseconds) && isInteger(value.startingLives) && typeof value.mode === 'string' }
function isLeaderboard(value: unknown): value is AsteroidsLeaderboard { return isRecord(value) && Array.isArray(value.entries) && value.entries.every(isLeaderboardEntry) }
function isLeaderboardEntry(value: unknown): boolean { return isRecord(value) && isInteger(value.rank) && typeof value.playerName === 'string' && isInteger(value.score) && isInteger(value.wave) }
function isGame(value: unknown): value is AsteroidsGameState { return isRecord(value) && typeof value.gameId === 'string' && isInteger(value.width) && isInteger(value.height) && isShip(value.ship) && Array.isArray(value.asteroids) && value.asteroids.every(isAsteroid) && Array.isArray(value.bullets) && value.bullets.every(isBullet) && Array.isArray(value.powerUps) && value.powerUps.every(isPowerUp) && isInteger(value.score) && isInteger(value.bestScore) && isInteger(value.lives) && isInteger(value.wave) && isInteger(value.tick) && isPhase(value.phase) && isEvent(value.lastEvent) && isInteger(value.scoreGained) && isInteger(value.rapidFireTicks) && typeof value.message === 'string' }
function isShip(value: unknown): value is AsteroidsShip { return isRecord(value) && isNumber(value.x) && isNumber(value.y) && isNumber(value.velocityX) && isNumber(value.velocityY) && isNumber(value.angle) && isInteger(value.invulnerabilityTicks) && isInteger(value.thrustTicks) }
function isAsteroid(value: unknown): value is Asteroid { return isRecord(value) && isInteger(value.id) && isNumber(value.x) && isNumber(value.y) && isNumber(value.velocityX) && isNumber(value.velocityY) && isNumber(value.radius) && isInteger(value.hitPoints) && isInteger(value.spriteVariant) && value.spriteVariant >= 0 && value.spriteVariant < 7 && isSize(value.size) && isKind(value.kind) }
function isBullet(value: unknown): boolean { return isRecord(value) && isInteger(value.id) && isNumber(value.x) && isNumber(value.y) && isNumber(value.velocityX) && isNumber(value.velocityY) && isInteger(value.remainingTicks) }
function isPowerUp(value: unknown): boolean { return isRecord(value) && isInteger(value.id) && isNumber(value.x) && isNumber(value.y) && isNumber(value.velocityX) && isNumber(value.velocityY) && isInteger(value.remainingTicks) && isPowerUpType(value.type) }
function isSize(value: unknown): value is AsteroidSize { return value === 'tiny' || value === 'small' || value === 'medium' || value === 'large' || value === 'huge' }
function isKind(value: unknown): value is AsteroidKind { return value === 'drifter' || value === 'hunter' }
function isPhase(value: unknown): value is AsteroidsPhase { return value === 'playing' || value === 'game-over' }
function isPowerUpType(value: unknown): value is AsteroidsPowerUpType { return value === 'shield' || value === 'rapid-fire' || value === 'extra-life' }
function isEvent(value: unknown): value is AsteroidsEvent { return value === 'started' || value === 'ticked' || value === 'rotated' || value === 'thrusted' || value === 'fired' || value === 'hit' || value === 'damaged' || value === 'wave-cleared' || value === 'power-up-collected' || value === 'no-op' || value === 'game-over' }
function isNumber(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) }
function isInteger(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

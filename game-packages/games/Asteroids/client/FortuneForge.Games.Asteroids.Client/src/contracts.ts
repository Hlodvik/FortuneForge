export type AsteroidsAction = 'tick' | 'rotate-left' | 'rotate-right' | 'thrust' | 'fire'
export type AsteroidsPhase = 'playing' | 'game-over'
export type AsteroidsEvent = 'started' | 'ticked' | 'rotated' | 'thrusted' | 'fired' | 'hit' | 'damaged' | 'wave-cleared' | 'power-up-collected' | 'no-op' | 'game-over'
export type AsteroidSize = 'tiny' | 'small' | 'medium' | 'large' | 'huge'
export type AsteroidKind = 'drifter' | 'hunter'
export type AsteroidsPowerUpType = 'shield' | 'rapid-fire' | 'extra-life'

export type AsteroidsShip = Readonly<{ x: number; y: number; velocityX: number; velocityY: number; angle: number; invulnerabilityTicks: number; thrustTicks: number }>
export type Asteroid = Readonly<{ id: number; x: number; y: number; velocityX: number; velocityY: number; radius: number; hitPoints: number; spriteVariant: number; size: AsteroidSize; kind: AsteroidKind }>
export type AsteroidsBullet = Readonly<{ id: number; x: number; y: number; velocityX: number; velocityY: number; remainingTicks: number }>
export type AsteroidsPowerUp = Readonly<{ id: number; x: number; y: number; velocityX: number; velocityY: number; remainingTicks: number; type: AsteroidsPowerUpType }>

export type AsteroidsStatus = Readonly<{ available: boolean; width: number; height: number; tickMilliseconds: number; startingLives: number; mode: string }>
export type AsteroidsLeaderboardEntry = Readonly<{ rank: number; playerName: string; score: number; wave: number }>
export type AsteroidsLeaderboard = Readonly<{ entries: readonly AsteroidsLeaderboardEntry[] }>

export type AsteroidsGameState = Readonly<{
  gameId: string
  width: number
  height: number
  ship: AsteroidsShip
  asteroids: readonly Asteroid[]
  bullets: readonly AsteroidsBullet[]
  powerUps: readonly AsteroidsPowerUp[]
  score: number
  bestScore: number
  lives: number
  wave: number
  tick: number
  phase: AsteroidsPhase
  lastEvent: AsteroidsEvent
  scoreGained: number
  rapidFireTicks: number
  message: string
}>

export interface AsteroidsGateway {
  getStatus(signal?: AbortSignal): Promise<AsteroidsStatus>
  startGame(seed?: number, signal?: AbortSignal): Promise<AsteroidsGameState>
  action(gameId: string, action: AsteroidsAction, signal?: AbortSignal): Promise<AsteroidsGameState>
  reset(gameId: string, seed?: number, signal?: AbortSignal): Promise<AsteroidsGameState>
  getLeaderboard(signal?: AbortSignal): Promise<AsteroidsLeaderboard>
  submitScore(gameId: string, playerName?: string, signal?: AbortSignal): Promise<AsteroidsLeaderboard>
}

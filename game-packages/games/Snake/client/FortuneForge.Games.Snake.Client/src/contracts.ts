export type SnakeDirection = 'up' | 'right' | 'down' | 'left'
export type SnakePhase = 'playing' | 'won' | 'lost'
export type SnakeEvent = 'started' | 'turned' | 'moved' | 'ate-food' | 'no-op' | 'won' | 'lost'

export type SnakePoint = Readonly<{ x: number; y: number }>
export type SnakeOptions = Readonly<{ width?: number; height?: number }>

export type SnakeStatus = Readonly<{
  available: boolean
  width: number
  height: number
  foodScore: number
  tickMilliseconds: number
  mode: string
}>

export type SnakeGameState = Readonly<{
  gameId: string
  width: number
  height: number
  body: readonly SnakePoint[]
  food: SnakePoint | null
  direction: SnakeDirection
  score: number
  bestScore: number
  moves: number
  length: number
  phase: SnakePhase
  lastEvent: SnakeEvent
  scoreGained: number
  message: string
}>

export interface SnakeGateway {
  getStatus(signal?: AbortSignal): Promise<SnakeStatus>
  startGame(seed?: number, signal?: AbortSignal, options?: SnakeOptions): Promise<SnakeGameState>
  turn(gameId: string, direction: SnakeDirection, signal?: AbortSignal): Promise<SnakeGameState>
  tick(gameId: string, signal?: AbortSignal): Promise<SnakeGameState>
  reset(gameId: string, seed?: number, signal?: AbortSignal, options?: SnakeOptions): Promise<SnakeGameState>
}

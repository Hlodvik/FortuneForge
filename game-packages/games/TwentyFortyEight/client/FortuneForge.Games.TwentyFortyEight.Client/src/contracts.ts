export type TwentyFortyEightDirection = 'up' | 'right' | 'down' | 'left'
export type TwentyFortyEightPhase = 'playing' | 'won' | 'lost'
export type TwentyFortyEightEvent = 'started' | 'moved' | 'no-move' | 'won' | 'lost' | 'undone'

export type TwentyFortyEightStatus = Readonly<{
  available: boolean
  size: number
  targetTile: number
  mode: string
}>

export type TwentyFortyEightGameState = Readonly<{
  gameId: string
  size: number
  tiles: readonly number[]
  score: number
  moves: number
  highestTile: number
  phase: TwentyFortyEightPhase
  canUndo: boolean
  lastEvent: TwentyFortyEightEvent
  scoreGained: number
  message: string
}>

export interface TwentyFortyEightGateway {
  getStatus(signal?: AbortSignal): Promise<TwentyFortyEightStatus>
  startGame(seed?: number, signal?: AbortSignal): Promise<TwentyFortyEightGameState>
  move(gameId: string, direction: TwentyFortyEightDirection, signal?: AbortSignal): Promise<TwentyFortyEightGameState>
  undo(gameId: string, signal?: AbortSignal): Promise<TwentyFortyEightGameState>
  reset(gameId: string, seed?: number, signal?: AbortSignal): Promise<TwentyFortyEightGameState>
}

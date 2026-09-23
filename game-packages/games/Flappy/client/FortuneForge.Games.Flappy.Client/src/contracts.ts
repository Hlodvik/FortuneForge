export type FlappyPhase = 'playing' | 'obstacle-collision' | 'ground-collision' | 'ceiling-collision'
export type FlappyObstacle = Readonly<{ id: number; x: number; gapTop: number; gapBottom: number }>
export type FlappyStatus = Readonly<{ available: boolean; tickMilliseconds: number; mode: string }>
export type FlappyGameState = Readonly<{
  gameId: string; width: number; height: number; birdX: number; birdY: number; birdRadius: number
  obstacleWidth: number; score: number; bestScore: number; level: number; phase: FlappyPhase
  obstacles: readonly FlappyObstacle[]
}>
export interface FlappyGateway {
  getStatus(signal?: AbortSignal): Promise<FlappyStatus>
  startGame(seed?: number, signal?: AbortSignal): Promise<FlappyGameState>
  step(gameId: string, flap: boolean, signal?: AbortSignal): Promise<FlappyGameState>
  reset(gameId: string, seed?: number, signal?: AbortSignal): Promise<FlappyGameState>
}

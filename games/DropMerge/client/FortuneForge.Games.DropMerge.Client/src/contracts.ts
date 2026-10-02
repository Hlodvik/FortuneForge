export type DropMergePhase = 'playing' | 'lost'
export type DropMergeEvent = 'started' | 'dropped' | 'no-move' | 'big-tile-reached' | 'lost' | 'undone'

export type DropMergeStatus = Readonly<{
  available: boolean
  columns: number
  rows: number
  firstBigTile: number
  mode: string
}>

export type DropMergeMergeTile = Readonly<{
  row: number
  column: number
  value: number
}>

export type DropMergeMergeStep = Readonly<{
  sources: readonly DropMergeMergeTile[]
  target: DropMergeMergeTile
  tilesAfter: readonly number[]
}>

export type DropMergeGameState = Readonly<{
  gameId: string
  columns: number
  rows: number
  tiles: readonly number[]
  currentTile: number
  nextTile: number
  score: number
  moves: number
  combo: number
  bestCombo: number
  totalMerges: number
  smallestTileClearCount: number
  nextBigTile: number
  highestTile: number
  emptyTileCount: number
  phase: DropMergePhase
  canUndo: boolean
  lastEvent: DropMergeEvent
  scoreGained: number
  mergeCount: number
  dropColumn: number
  dropRow: number
  removedTile: number
  introducedTile: number
  mergeSteps: readonly DropMergeMergeStep[]
  tempoLevel: number
  dropDurationMilliseconds: number
  dropTimerMilliseconds: number
  message: string
}>

export interface DropMergeGateway {
  getStatus(signal?: AbortSignal): Promise<DropMergeStatus>
  startGame(seed?: number, signal?: AbortSignal): Promise<DropMergeGameState>
  drop(gameId: string, column: number, signal?: AbortSignal): Promise<DropMergeGameState>
  undo(gameId: string, signal?: AbortSignal): Promise<DropMergeGameState>
  reset(gameId: string, seed?: number, signal?: AbortSignal): Promise<DropMergeGameState>
}

import type { DropMergeEvent, DropMergeGameState, DropMergeGateway, DropMergePhase, DropMergeStatus } from './contracts'

export type DropMergeFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>

export class DropMergeGatewayError extends Error {
  readonly code?: string
  readonly status?: number

  constructor(message: string, code?: string, status?: number) {
    super(message)
    this.name = 'DropMergeGatewayError'
    this.code = code
    this.status = status
  }
}

export class HttpDropMergeGateway implements DropMergeGateway {
  constructor(
    private readonly basePath = '/api/games/drop-merge',
    private readonly fetcher: DropMergeFetch = fetch,
  ) {}

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }

  startGame(seed?: number, signal?: AbortSignal) {
    return this.request('/games', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  drop(gameId: string, column: number, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/drop`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ column }), signal }, isGame)
  }

  undo(gameId: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/undo`, { method: 'POST', signal }, isGame)
  }

  reset(gameId: string, seed?: number, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/reset`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(seed === undefined ? {} : { seed }), signal }, isGame)
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await this.fetcher(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new DropMergeGatewayError(error?.message ?? `The Drop Merge request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new DropMergeGatewayError('The Drop Merge service returned an invalid response.')
    return value
  }
}

const jsonHeaders = { 'content-type': 'application/json' }

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is DropMergeStatus { return isRecord(value) && typeof value.available === 'boolean' && value.columns === 7 && value.rows === 7 && Number.isInteger(value.firstBigTile) && typeof value.mode === 'string' }
function isGame(value: unknown): value is DropMergeGameState {
  if (!isRecord(value)) return false
  const tiles = value.tiles
  const currentTile = value.currentTile
  const nextTile = value.nextTile
  return typeof value.gameId === 'string' && value.columns === 7 && value.rows === 7 && Array.isArray(tiles) && tiles.length === 49 && tiles.every(tile => Number.isInteger(tile) && tile >= 0) && isPositiveInteger(currentTile) && isPositiveInteger(nextTile) && Number.isInteger(value.score) && Number.isInteger(value.moves) && Number.isInteger(value.combo) && Number.isInteger(value.bestCombo) && Number.isInteger(value.totalMerges) && Number.isInteger(value.smallestTileClearCount) && Number.isInteger(value.nextBigTile) && Number.isInteger(value.highestTile) && Number.isInteger(value.emptyTileCount) && isPhase(value.phase) && typeof value.canUndo === 'boolean' && isEvent(value.lastEvent) && Number.isInteger(value.scoreGained) && Number.isInteger(value.mergeCount) && Number.isInteger(value.dropColumn) && Number.isInteger(value.dropRow) && Number.isInteger(value.removedTile) && Number.isInteger(value.introducedTile) && isMergeSteps(value.mergeSteps) && Number.isInteger(value.tempoLevel) && Number.isInteger(value.dropDurationMilliseconds) && Number.isInteger(value.dropTimerMilliseconds) && typeof value.message === 'string'
}
function isPhase(value: unknown): value is DropMergePhase { return value === 'playing' || value === 'lost' }
function isEvent(value: unknown): value is DropMergeEvent { return value === 'started' || value === 'dropped' || value === 'no-move' || value === 'big-tile-reached' || value === 'lost' || value === 'undone' }
function isMergeSteps(value: unknown): boolean { return Array.isArray(value) && value.every(step => isRecord(step) && Array.isArray(step.sources) && step.sources.length >= 2 && step.sources.every(isMergeTile) && isMergeTile(step.target) && Array.isArray(step.tilesAfter) && step.tilesAfter.length === 49 && step.tilesAfter.every(tile => Number.isInteger(tile) && tile >= 0)) }
function isMergeTile(value: unknown): boolean { return isRecord(value) && Number.isInteger(value.row) && Number.isInteger(value.column) && isPositiveInteger(value.value) }
function isPositiveInteger(value: unknown): value is number { const numeric = typeof value === 'number' ? value : Number.NaN; return Number.isInteger(numeric) && numeric > 0 }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

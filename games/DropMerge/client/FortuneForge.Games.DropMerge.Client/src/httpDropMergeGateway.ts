import type { GameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
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
    private readonly recovery?: GameCommandRecovery,
    private readonly expectedAccountId?: string,
  ) {}

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }

  async getGame(gameId: string, signal?: AbortSignal) {
    const current = await this.request(`/games/${encodeURIComponent(gameId)}`, { signal }, isGame)
    if (this.recovery && !this.recovery.read().pending) this.recovery.setActiveBoardId(current.gameId)
    return current
  }

  async recoverGame(signal?: AbortSignal) {
    const saved = this.recovery?.read()
    if (saved?.pending) {
      const pending = saved.pending
      if (!pending.path.startsWith(this.basePath + '/')) throw new DropMergeGatewayError('The saved command belongs to a different game service.')
      const legacyId = this.legacyGameId(pending.path)
      if (legacyId) {
        try {
          const current = await this.getGame(legacyId, signal)
          this.recovery?.complete(pending, current.gameId)
          return current
        } catch (reason) {
          if (reason instanceof DropMergeGatewayError && reason.status === 404) this.recovery?.complete(pending, null)
          throw reason
        }
      }
      const recovered = await this.request(pending.path.slice(this.basePath.length),
        { method: 'POST', headers: jsonHeaders, body: pending.body, signal }, isGame)
      return this.getGame(recovered.gameId, signal)
    }
    return saved?.activeBoardId ? this.getGame(saved.activeBoardId, signal) : this.startGame(undefined, signal)
  }

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

  private legacyGameId(path: string): string | undefined {
    if (!path.startsWith(this.basePath + '/')) return undefined
    return /^\/games\/([0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12})\//i.exec(path.slice(this.basePath.length))?.[1]
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const target = this.basePath + path
    const body = String(init.body ?? '')
    const prior = init.method === 'POST' ? this.recovery?.read().pending : null
    if (prior?.path === target && prior.body === body && this.legacyGameId(prior.path)) {
      const recovered: unknown = await this.recoverGame(init.signal ?? undefined)
      if (!validate(recovered)) throw new DropMergeGatewayError('The saved board could not be recovered.')
      return recovered
    }
    const command = init.method === 'POST' ? this.recovery?.prepare(target, body) : undefined
    if (command || this.expectedAccountId) {
      const headers = new Headers(init.headers)
      if (command) headers.set('Idempotency-Key', command.idempotencyKey)
      if (this.expectedAccountId) headers.set('X-FortuneForge-Expected-Account', this.expectedAccountId)
      init = { ...init, headers, ...(command ? { body: command.body } : {}) }
    }
    const response = await this.fetcher(target, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      if (command && response.status < 500 && ![401, 403, 408, 425, 429].includes(response.status)) this.recovery?.complete(command)
      const error = isError(value) ? value : null
      throw new DropMergeGatewayError(error?.message ?? `The Drop Merge request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new DropMergeGatewayError('The Drop Merge service returned an invalid response.')
    if (command && isGame(value)) this.recovery?.complete(command, value.gameId)
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

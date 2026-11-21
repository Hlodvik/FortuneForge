import type { GameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import type { TwentyFortyEightDirection, TwentyFortyEightEvent, TwentyFortyEightGameState, TwentyFortyEightGateway, TwentyFortyEightPhase, TwentyFortyEightStatus } from './contracts'

export type TwentyFortyEightFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>

export class TwentyFortyEightGatewayError extends Error {
  readonly code?: string
  readonly status?: number

  constructor(message: string, code?: string, status?: number) {
    super(message)
    this.name = 'TwentyFortyEightGatewayError'
    this.code = code
    this.status = status
  }
}

export class HttpTwentyFortyEightGateway implements TwentyFortyEightGateway {
  constructor(
    private readonly basePath = '/api/games/2048',
    private readonly fetcher: TwentyFortyEightFetch = fetch,
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
      if (!pending.path.startsWith(this.basePath + '/')) throw new TwentyFortyEightGatewayError('The saved command belongs to a different game service.')
      const legacyId = this.legacyGameId(pending.path)
      if (legacyId) {
        try {
          const current = await this.getGame(legacyId, signal)
          this.recovery?.complete(pending, current.gameId)
          return current
        } catch (reason) {
          if (reason instanceof TwentyFortyEightGatewayError && reason.status === 404) this.recovery?.complete(pending, null)
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

  move(gameId: string, direction: TwentyFortyEightDirection, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/move`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ direction }), signal }, isGame)
  }

  undo(gameId: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/undo`, { method: 'POST', signal }, isGame)
  }

  continueGame(gameId: string, signal?: AbortSignal) {
    return this.request(`/games/${encodeURIComponent(gameId)}/continue`, { method: 'POST', signal }, isGame)
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
      if (!validate(recovered)) throw new TwentyFortyEightGatewayError('The saved board could not be recovered.')
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
      throw new TwentyFortyEightGatewayError(error?.message ?? `The 2048 request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new TwentyFortyEightGatewayError('The 2048 service returned an invalid response.')
    if (command && isGame(value)) this.recovery?.complete(command, value.gameId)
    return value
  }
}

const jsonHeaders = { 'content-type': 'application/json' }

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is TwentyFortyEightStatus { return isRecord(value) && typeof value.available === 'boolean' && Number.isInteger(value.size) && Number.isInteger(value.targetTile) && typeof value.mode === 'string' }
function isGame(value: unknown): value is TwentyFortyEightGameState {
  return isRecord(value) && typeof value.gameId === 'string' && typeof value.size === 'number' && Number.isInteger(value.size) && Array.isArray(value.tiles) && value.tiles.length === value.size * value.size && value.tiles.every(tile => Number.isInteger(tile) && tile >= 0) && Number.isInteger(value.score) && Number.isInteger(value.moves) && Number.isInteger(value.highestTile) && isPhase(value.phase) && typeof value.canUndo === 'boolean' && isEvent(value.lastEvent) && Number.isInteger(value.scoreGained) && typeof value.message === 'string'
}
function isPhase(value: unknown): value is TwentyFortyEightPhase { return value === 'playing' || value === 'won' || value === 'lost' }
function isEvent(value: unknown): value is TwentyFortyEightEvent { return value === 'started' || value === 'moved' || value === 'no-move' || value === 'won' || value === 'lost' || value === 'undone' || value === 'continued' }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

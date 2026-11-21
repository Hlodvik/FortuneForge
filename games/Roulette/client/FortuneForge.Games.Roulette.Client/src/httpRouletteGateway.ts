import { createGameCommandRecovery, PendingGameCommandError, type GameCommandRecovery, type PendingGameCommand } from '@fortuneforge/client-presentation/game-command-recovery'
import { RouletteGatewayError, type RouletteBet, type RouletteBetKind, type RouletteGateway, type RouletteRound, type RouletteStatus } from './contracts'

export type RouletteRecoveryOptions = Readonly<{ accountId?: string; storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>; createIdempotencyKey?: () => string }>

export class HttpRouletteGateway implements RouletteGateway {
  readonly basePath: string
  private readonly recovery: GameCommandRecovery
  private readonly revisions = new Map<string, number>()
  constructor(basePath = '/api/games/roulette', private readonly fetcher: typeof fetch = fetch, private readonly options: RouletteRecoveryOptions = {}) {
    this.basePath = basePath.replace(/\/$/, '')
    const memory = new Map<string, string>()
    this.recovery = createGameCommandRecovery({ accountId: options.accountId ?? 'local-roulette', gameId: 'roulette', routePrefix: this.basePath,
      storage: options.storage ?? (options.accountId ? globalThis.sessionStorage : { getItem: key => memory.get(key) ?? null, setItem: (key, value) => { memory.set(key, value) }, removeItem: key => { memory.delete(key) } }),
      createIdempotencyKey: options.createIdempotencyKey })
  }
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  async getRound(roundId: string, signal?: AbortSignal) {
    if (this.recovery.read().pending) return this.recover(signal)
    return this.remember(await this.request(`/rounds/${encodeURIComponent(roundId)}`, { signal }, isRound, roundId))
  }
  async openRound(signal?: AbortSignal) {
    if (this.recovery.read().pending) return this.recover(signal)
    const active = this.recovery.read().activeBoardId
    if (active) {
      try { const round = await this.getRound(active, signal); if (round.phase === 'open') return round }
      catch (error) { if (!(error instanceof RouletteGatewayError) || error.status !== 404) throw error }
    }
    return this.mutate('/rounds', '{}', signal)
  }
  placeBet(roundId: string, bet: Omit<RouletteBet, 'betIndex' | 'playerId'>, signal?: AbortSignal) {
    return this.mutate(`/rounds/${encodeURIComponent(roundId)}/bets`, JSON.stringify({ ...bet, expectedRevision: this.revisions.get(roundId) }), signal, roundId)
  }
  removeBet(roundId: string, betIndex: number, signal?: AbortSignal) {
    return this.mutate(`/rounds/${encodeURIComponent(roundId)}/bets/${betIndex}`, this.actionBody(roundId), signal, roundId)
  }
  clearBets(roundId: string, signal?: AbortSignal) { return this.mutate(`/rounds/${encodeURIComponent(roundId)}/bets`, this.actionBody(roundId), signal, roundId) }
  spin(roundId: string, signal?: AbortSignal) { return this.mutate(`/rounds/${encodeURIComponent(roundId)}/spin`, this.actionBody(roundId), signal, roundId) }
  private actionBody(roundId: string) { return JSON.stringify({ expectedRevision: this.revisions.get(roundId) }) }
  private recover(signal?: AbortSignal) {
    const command = this.recovery.read().pending
    if (!command) throw new RouletteGatewayError('There is no pending Roulette command.')
    return this.send(command, signal)
  }
  private mutate(path: string, body: string, signal?: AbortSignal, roundId?: string) {
    let command: PendingGameCommand
    try { command = this.recovery.prepare(this.basePath + path, body) }
    catch (error) {
      if (error instanceof PendingGameCommandError) throw new RouletteGatewayError('Recover the pending Roulette command before submitting another action.', 'roulette-recovery-required')
      throw error
    }
    return this.send(command, signal, roundId)
  }
  private async send(command: PendingGameCommand, signal?: AbortSignal, roundId?: string) {
    const path = command.path.slice(this.basePath.length)
    const body: unknown = JSON.parse(command.body)
    const method = /\/bets(?:\/\d+)?$/.test(path) && (!isRecord(body) || !('kind' in body)) ? 'DELETE' : 'POST'
    try {
      const result = this.remember(await this.request(path, { method, body: command.body, signal,
        headers: { 'content-type': 'application/json', 'Idempotency-Key': command.idempotencyKey } }, isRound, roundId))
      this.recovery.complete(command, result.roundId)
      return result
    } catch (error) {
      if (error instanceof RouletteGatewayError && error.status >= 400 && error.status < 500 && error.status !== 401 && error.status !== 429 && !(error.status === 403 && error.code === 'game-session-changed')) this.recovery.complete(command)
      throw error
    }
  }
  private remember(round: RouletteRound) {
    if (round.revision !== undefined) this.revisions.set(round.roundId, round.revision)
    return round
  }
  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T, expectedRoundId?: string): Promise<T> {
    const headers = new Headers(init.headers)
    if (this.options.accountId) headers.set('X-FortuneForge-Expected-Account', this.options.accountId)
    const request = this.fetcher
    const response = await request(`${this.basePath}${path}`, { ...init, headers })
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) { const error = isError(value) ? value : null; throw new RouletteGatewayError(error?.message ?? `The Roulette request failed (${response.status}).`, error?.code, response.status) }
    if (!validate(value) || (expectedRoundId && (!isRecord(value) || value.roundId !== expectedRoundId))) throw new RouletteGatewayError('The Roulette service returned an invalid response.')
    return value
  }
}

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isStatus(value: unknown): value is RouletteStatus { return isRecord(value) && typeof value.available === 'boolean' && positive(value.minimumStake) && positive(value.maximumStake) && value.maximumStake >= value.minimumStake && positive(value.stakeIncrement) && nonnegative(value.startingBalance) && typeof value.mode === 'string' }
function isRound(value: unknown): value is RouletteRound { return isRecord(value) && typeof value.roundId === 'string' && (value.revision === undefined || Number.isSafeInteger(value.revision) && Number(value.revision) >= 0) && nonnegative(value.balance) && (value.phase === 'open' || value.phase === 'settled') && Array.isArray(value.bets) && value.bets.every(isBet) && (value.winningPocket === null || isPocket(value.winningPocket)) && Array.isArray(value.settlements) && value.settlements.every(isSettlement) && value.bets.every((bet, index) => bet.betIndex === index) && (value.tableId === undefined || typeof value.tableId === 'string') && (value.roundNumber === undefined || Number.isInteger(value.roundNumber) && Number(value.roundNumber) > 0) && (value.players === undefined || Array.isArray(value.players) && value.players.every(isPlayer)) && (value.phase === 'open' ? value.winningPocket === null && value.settlements.length === 0 : isPocket(value.winningPocket) && value.settlements.length === value.bets.length) }
function isBet(value: unknown): value is RouletteBet { return isRecord(value) && Number.isInteger(value.betIndex) && Number(value.betIndex) >= 0 && typeof value.playerId === 'string' && isKind(value.kind) && positive(value.stake) && (value.number === null || isPocket(value.number)) && Array.isArray(value.numbers) && value.numbers.every(isPocket) }
function isSettlement(value: unknown): boolean { return isRecord(value) && typeof value.playerId === 'string' && isKind(value.kind) && typeof value.stake === 'number' && typeof value.won === 'boolean' && nonnegative(value.totalReturn) }
function isPlayer(value: unknown): boolean { return isRecord(value) && typeof value.seatId === 'string' && typeof value.displayName === 'string' && typeof value.isCurrentPlayer === 'boolean' }
function isKind(value: unknown): value is RouletteBetKind { return value === 'straight' || value === 'split' || value === 'street' || value === 'corner' || value === 'six-line' || value === 'column' || value === 'dozen' || value === 'red' || value === 'black' || value === 'even' || value === 'odd' || value === 'low' || value === 'high' }
function isPocket(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 36 }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }

function nonnegative(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function positive(value: unknown): value is number { return nonnegative(value) && value > 0 }

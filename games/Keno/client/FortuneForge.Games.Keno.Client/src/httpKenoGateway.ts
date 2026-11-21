import { KenoGatewayError, type KenoGateway, type KenoRequestOptions, type KenoRound, type KenoRoundRequest, type KenoStatus, type KenoTicket } from './contracts'

export class HttpKenoGateway implements KenoGateway {
  readonly basePath: string
  private readonly requestFn: typeof fetch

  constructor(basePath = '/api/games/keno', requestFn: typeof fetch = fetch, private readonly expectedAccountId?: string) {
    this.basePath = basePath.replace(/\/$/, '')
    this.requestFn = requestFn
  }

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }

  getRound(roundId: string, signal?: AbortSignal) {
    if (!/^[0-9a-f]{64}$/i.test(roundId))
      throw new KenoGatewayError('The Keno round identifier is invalid.', 'keno-invalid-request')
    return this.request(`/rounds/${roundId}`, { signal }, (value): value is KenoRound =>
      isRound(value) && value.roundId === roundId)
  }

  createRound(request: KenoRoundRequest, options: KenoRequestOptions = {}) {
    if (!isTicket(request.ticket) || !isPositiveMoney(request.wager))
      throw new KenoGatewayError('The Keno ticket is invalid.', 'keno-invalid-ticket')

    return this.request('/rounds', jsonPost(request, options.signal, options.idempotencyKey ?? createIdempotencyKey()), value => isRound(value, request))
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const boundInit = this.expectedAccountId ? { ...init, headers: new Headers(init.headers) } : init
    if (this.expectedAccountId) (boundInit.headers as Headers).set('X-FortuneForge-Expected-Account', this.expectedAccountId)
    const response = await this.requestFn(`${this.basePath}${path}`, boundInit)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new KenoGatewayError(error?.message ?? error?.error ?? `The Keno request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new KenoGatewayError('The Keno service returned an invalid response.')
    return value
  }
}

function jsonPost(body: object, signal: AbortSignal | undefined, idempotencyKey: string): RequestInit {
  return { method: 'POST', headers: { 'content-type': 'application/json', 'Idempotency-Key': idempotencyKey }, body: JSON.stringify(body), signal }
}

function isStatus(value: unknown): value is KenoStatus {
  return isRecord(value) && typeof value.available === 'boolean' &&
    isPositiveMoney(value.minimumWager) && isPositiveMoney(value.maximumWager) && value.maximumWager >= value.minimumWager &&
    isPositiveMoney(value.wagerIncrement) && isNonNegativeFinite(value.balance) &&
    typeof value.mode === 'string' && value.mode.trim().length > 0 && isPaytable(value.paytable)
}

function isRound(value: unknown, request?: KenoRoundRequest): value is KenoRound {
  return isRecord(value) && isNonBlankString(value.roundId) && isNonNegativeFinite(value.balance) && value.phase === 'completed' && isTicket(value.ticket) && (!request || sameNumbers(value.ticket.numbers, request.ticket.numbers)) &&
    isDraw(value.draw) && isHitCount(value.hitCount, value.ticket, value.draw) &&
    isPositiveMoney(value.wager) && (!request || value.wager === request.wager) && isNonNegativeFinite(value.payout) && typeof value.net === 'number' && Number.isFinite(value.net) &&
    Math.abs(value.net - (value.payout - value.wager)) < 0.001 && typeof value.outcome === 'string'
}

function isPaytable(value: unknown): value is KenoStatus['paytable'] {
  return Array.isArray(value) && value.length > 0 && value.every(tier => isRecord(tier) &&
    typeof tier.spots === 'number' && Number.isInteger(tier.spots) && tier.spots >= 1 && tier.spots <= 10 &&
    typeof tier.hits === 'number' && Number.isInteger(tier.hits) && tier.hits >= 0 && tier.hits <= tier.spots &&
    typeof tier.multiplier === 'number' && Number.isFinite(tier.multiplier) && tier.multiplier > 0)
}

function isTicket(value: unknown): value is KenoTicket {
  return isRecord(value) && isNumberSelection(value.numbers, 1, 10)
}

function isDraw(value: unknown): value is Readonly<{ numbers: readonly number[] }> {
  return isRecord(value) && isNumberSelection(value.numbers, 20, 20)
}

function isNumberSelection(value: unknown, minimumLength: number, maximumLength: number): value is readonly number[] {
  return Array.isArray(value) && value.length >= minimumLength && value.length <= maximumLength &&
    value.every(isKenoNumber) && new Set(value).size === value.length
}

function isHitCount(value: unknown, ticket: KenoTicket, draw: Readonly<{ numbers: readonly number[] }>): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value === ticket.numbers.filter(number => draw.numbers.includes(number)).length
}

function sameNumbers(left: readonly number[], right: readonly number[]): boolean {
  return left.length === right.length && left.every((number, index) => number === right[index])
}

function isKenoNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 80
}

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null }
function isNonBlankString(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0 }
function isNonNegativeFinite(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function isPositiveMoney(value: unknown): value is number { return isNonNegativeFinite(value) && value > 0 }
function isError(value: unknown): value is { code: string; message?: string; error?: string } { return isRecord(value) && typeof value.code === 'string' && (typeof value.message === 'string' || typeof value.error === 'string') }
function createIdempotencyKey(): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `keno-${random}`
}

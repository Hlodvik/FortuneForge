import { KenoGatewayError, type KenoGateway, type KenoRequestOptions, type KenoRound, type KenoRoundRequest, type KenoStatus, type KenoTicket } from './contracts'

export class HttpKenoGateway implements KenoGateway {
  readonly basePath: string
  private readonly requestFn: typeof fetch

  constructor(basePath = '/api/games/keno', requestFn: typeof fetch = fetch) {
    this.basePath = basePath.replace(/\/$/, '')
    this.requestFn = requestFn
  }

  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }

  createRound(request: KenoRoundRequest, options: KenoRequestOptions = {}) {
    if (!isTicket(request.ticket))
      throw new KenoGatewayError('The Keno ticket is invalid.', 'keno-invalid-ticket')

    return this.request('/rounds', jsonPost(request, options.signal, options.idempotencyKey ?? createIdempotencyKey()), value => isRound(value, request.ticket))
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await this.requestFn(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new KenoGatewayError(error?.message ?? `The Keno request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new KenoGatewayError('The Keno service returned an invalid response.')
    return value
  }
}

function jsonPost(body: object, signal: AbortSignal | undefined, idempotencyKey: string): RequestInit {
  return { method: 'POST', headers: { 'content-type': 'application/json', 'Idempotency-Key': idempotencyKey }, body: JSON.stringify(body), signal }
}

function isStatus(value: unknown): value is KenoStatus {
  return isRecord(value) && typeof value.available === 'boolean' && isNonNegativeFinite(value.balance) && typeof value.mode === 'string' && value.mode.trim().length > 0
}

function isRound(value: unknown, ticket: KenoTicket): value is KenoRound {
  return isRecord(value) && isNonBlankString(value.roundId) && isNonNegativeFinite(value.balance) && value.phase === 'completed' && isTicket(value.ticket) && sameNumbers(value.ticket.numbers, ticket.numbers) &&
    isDraw(value.draw) && isHitCount(value.hitCount, value.ticket, value.draw) &&
    (value.outcome === null || typeof value.outcome === 'string')
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
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }
function createIdempotencyKey(): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `keno-${random}`
}

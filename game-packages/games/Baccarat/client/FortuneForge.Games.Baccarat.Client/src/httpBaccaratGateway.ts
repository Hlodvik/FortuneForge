import {
  BaccaratGatewayError,
  type BaccaratBetDisposition,
  type BaccaratBetSide,
  type BaccaratCard,
  type BaccaratGateway,
  type BaccaratRound,
  type BaccaratRequestOptions,
  type BaccaratRoundOutcome,
  type BaccaratStatus,
} from './contracts'

export class HttpBaccaratGateway implements BaccaratGateway {
  readonly basePath: string
  private readonly requestFn: typeof fetch

  constructor(basePath = '/api/games/baccarat', requestFn: typeof fetch = fetch) {
    this.basePath = basePath.replace(/\/$/, '')
    this.requestFn = requestFn
  }

  getStatus(signal?: AbortSignal) {
    return this.request('/status', { signal }, isStatus)
  }

  createRound(betSide: BaccaratBetSide, stake: number, options: BaccaratRequestOptions = {}) {
    return this.request('/rounds', jsonPost({ betSide, stake }, options.signal, options.idempotencyKey ?? createIdempotencyKey()), isRound)
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await this.requestFn(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new BaccaratGatewayError(
        error?.message ?? `The Baccarat request failed (${response.status}).`,
        error?.code,
        response.status,
      )
    }
    if (!validate(value)) throw new BaccaratGatewayError('The Baccarat service returned an invalid response.')
    return value
  }
}

function jsonPost(body: object, signal: AbortSignal | undefined, idempotencyKey: string): RequestInit {
  return { method: 'POST', headers: { 'content-type': 'application/json', 'Idempotency-Key': idempotencyKey }, body: JSON.stringify(body), signal }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isStatus(value: unknown): value is BaccaratStatus {
  return isRecord(value) && typeof value.available === 'boolean' &&
    isPositiveFinite(value.minimumStake) && isPositiveFinite(value.maximumStake) &&
    isPositiveFinite(value.stakeIncrement) && isNonNegativeFinite(value.balance) &&
    value.minimumStake <= value.maximumStake && typeof value.mode === 'string'
}

function isRound(value: unknown): value is BaccaratRound {
  if (!isRecord(value) || typeof value.roundId !== 'string' || value.roundId.trim().length === 0 ||
    !isNonNegativeFinite(value.balance) || !isBetSide(value.betSide) || !isPositiveFinite(value.stake) ||
    value.phase !== 'settled' || !isBaccaratCards(value.playerCards) || !isBaccaratCards(value.bankerCards) ||
    !isTotal(value.playerTotal) || !isTotal(value.bankerTotal) || !isOutcome(value.outcome) ||
    typeof value.endedOnNatural !== 'boolean' || !isDisposition(value.disposition) ||
    !isFiniteNumber(value.profit) || !isNonNegativeFinite(value.totalReturn) ||
    value.shoeCardsUsed != null && !isShoeCount(value.shoeCardsUsed) ||
    value.shoeCardsRemaining != null && !isShoeCount(value.shoeCardsRemaining) ||
    value.shoeCardsUsed != null && value.shoeCardsRemaining != null && value.shoeCardsUsed + value.shoeCardsRemaining !== 416) return false

  return nearlyEqual(value.profit, value.totalReturn - value.stake)
}

function isShoeCount(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 416 }

function isBaccaratCards(value: unknown): value is readonly BaccaratCard[] {
  return Array.isArray(value) && value.length >= 2 && value.length <= 3 && value.every(isCard)
}

function isCard(value: unknown): value is BaccaratCard {
  return isRecord(value) && isCardRank(value.rank) && isCardSuit(value.suit)
}

function isTotal(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 9
}

function isPositiveFinite(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function isNonNegativeFinite(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function nearlyEqual(left: number, right: number): boolean {
  return Math.abs(left - right) <= Number.EPSILON * Math.max(1, Math.abs(left), Math.abs(right)) * 16
}

function isBetSide(value: unknown): value is BaccaratBetSide {
  return value === 'player' || value === 'banker' || value === 'tie'
}

function isOutcome(value: unknown): value is BaccaratRoundOutcome {
  return value === 'player' || value === 'banker' || value === 'tie'
}

function isDisposition(value: unknown): value is BaccaratBetDisposition {
  return value === 'win' || value === 'loss' || value === 'push'
}

function isCardRank(value: unknown): value is BaccaratCard['rank'] {
  return value === 'ace' || value === 'two' || value === 'three' || value === 'four' ||
    value === 'five' || value === 'six' || value === 'seven' || value === 'eight' ||
    value === 'nine' || value === 'ten' || value === 'jack' || value === 'queen' || value === 'king'
}

function isCardSuit(value: unknown): value is BaccaratCard['suit'] {
  return value === 'clubs' || value === 'diamonds' || value === 'hearts' || value === 'spades'
}

function isError(value: unknown): value is { code: string; message: string } {
  return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string'
}

function createIdempotencyKey(): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `baccarat-${random}`
}

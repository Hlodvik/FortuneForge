import {
  VideoPokerGatewayError,
  type VideoPokerCard,
  type VideoPokerCardPosition,
  type VideoPokerGateway,
  type VideoPokerHandRank,
  type VideoPokerRound,
  type VideoPokerRequestOptions,
  type VideoPokerStatus,
} from './contracts'

export class HttpVideoPokerGateway implements VideoPokerGateway {
  readonly basePath: string
  private readonly requestFn: typeof fetch

  constructor(basePath = '/api/games/video-poker', requestFn: typeof fetch = fetch) {
    this.basePath = basePath.replace(/\/$/, '')
    this.requestFn = requestFn
  }

  getStatus(signal?: AbortSignal) {
    return this.request('/status', { signal }, isStatus)
  }

  getRound(roundId: string, signal?: AbortSignal) {
    return this.request(`/rounds/${encodeURIComponent(roundId)}`, { signal }, isRound)
  }

  createRound(coinsWagered: number, options: VideoPokerRequestOptions = {}) {
    return this.request(
      '/rounds',
      jsonPost({ coinsWagered }, options.signal, options.idempotencyKey ?? createIdempotencyKey('video-poker-deal')),
      isRound,
    )
  }

  draw(roundId: string, heldPositions: readonly VideoPokerCardPosition[], options: VideoPokerRequestOptions = {}) {
    return this.request(
      `/rounds/${encodeURIComponent(roundId)}/draw`,
      jsonPost({ heldPositions }, options.signal, options.idempotencyKey ?? createIdempotencyKey('video-poker-draw')),
      isRound,
    )
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    const response = await this.requestFn(`${this.basePath}${path}`, init)
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new VideoPokerGatewayError(
        error?.message ?? `The Video Poker request failed (${response.status}).`,
        error?.code,
        response.status,
      )
    }
    if (!validate(value)) {
      throw new VideoPokerGatewayError('The Video Poker service returned an invalid response.')
    }
    return value
  }
}

function jsonPost(body: object, signal: AbortSignal | undefined, idempotencyKey: string): RequestInit {
  return {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(body),
    signal,
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isStatus(value: unknown): value is VideoPokerStatus {
  return isRecord(value) &&
    typeof value.available === 'boolean' &&
    isWager(value.minimumCoinsWagered) &&
    isWager(value.maximumCoinsWagered) &&
    isMoney(value.coinValue) && value.coinValue > 0 &&
    isMoney(value.balance) && value.balance >= 0 &&
    value.minimumCoinsWagered <= value.maximumCoinsWagered
}

function isRound(value: unknown): value is VideoPokerRound {
  return isRecord(value) &&
    typeof value.roundId === 'string' && value.roundId.length > 0 &&
    isMoney(value.balance) &&
    isWager(value.coinsWagered) &&
    isMoney(value.wager) && value.wager > 0 &&
    isPhase(value.phase) &&
    isFiveCards(value.initialCards) &&
    isPositions(value.heldPositions) &&
    (value.finalCards === null || isFiveCards(value.finalCards)) &&
    (value.handRank === null || isHandRank(value.handRank)) &&
    (value.payout === null || (isMoney(value.payout) && value.payout >= 0)) &&
    isRoundStateConsistent(value)
}

function isRoundStateConsistent(value: Record<string, unknown>): boolean {
  return value.phase === 'awaiting-draw'
    ? value.finalCards === null && value.handRank === null && value.payout === null
    : value.finalCards !== null && value.handRank !== null && value.payout !== null
}

function isFiveCards(value: unknown): value is readonly VideoPokerCard[] {
  return Array.isArray(value) && value.length === 5 && value.every(isCard) &&
    new Set(value.map(card => `${card.rank}|${card.suit}`)).size === 5
}

function isCard(value: unknown): value is VideoPokerCard {
  return isRecord(value) && isCardRank(value.rank) && isCardSuit(value.suit)
}

function isPositions(value: unknown): value is readonly VideoPokerCardPosition[] {
  return Array.isArray(value) && value.every(isPosition) && new Set(value).size === value.length
}

function isPosition(value: unknown): value is VideoPokerCardPosition {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 4
}

function isWager(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 5
}

function isMoney(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function isPhase(value: unknown): value is VideoPokerRound['phase'] {
  return value === 'awaiting-draw' || value === 'completed'
}

function isCardRank(value: unknown): value is VideoPokerCard['rank'] {
  return value === 'ace' || value === 'two' || value === 'three' || value === 'four' ||
    value === 'five' || value === 'six' || value === 'seven' || value === 'eight' ||
    value === 'nine' || value === 'ten' || value === 'jack' || value === 'queen' || value === 'king'
}

function isCardSuit(value: unknown): value is VideoPokerCard['suit'] {
  return value === 'clubs' || value === 'diamonds' || value === 'hearts' || value === 'spades'
}

function isHandRank(value: unknown): value is VideoPokerHandRank {
  return value === 'no-win' || value === 'pair' || value === 'two-pair' ||
    value === 'three-of-a-kind' || value === 'straight' || value === 'flush' ||
    value === 'full-house' || value === 'four-of-a-kind' || value === 'straight-flush' ||
    value === 'royal-flush'
}

function isError(value: unknown): value is { code: string; message: string } {
  return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string'
}

function createIdempotencyKey(prefix: string): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `${prefix}-${random}`
}

import { AndarBaharGatewayError, type AndarBaharCard, type AndarBaharGateway, type AndarBaharRequestOptions, type AndarBaharRound, type AndarBaharSide, type AndarBaharStatus } from './contracts'

const invalidMessage = 'The table returned an incomplete result. Restore this deal before playing again.'
const idempotencyKeyPattern = /^[A-Za-z0-9_-]{16,128}$/
const roundIdPattern = /^[A-Za-z0-9_-]{1,128}$/

export class HttpAndarBaharGateway implements AndarBaharGateway {
  constructor(readonly basePath = '/api/games/andar-bahar', private readonly requestFn: typeof fetch = fetch) {}

  getStatus(signal?: AbortSignal) {
    return this.request('/status', { signal }, isStatus)
  }

  getLatestRound(signal?: AbortSignal) {
    return this.request('/rounds/latest', { signal }, (value): value is AndarBaharRound | null => value === null || isRound(value), true)
  }

  async getRound(roundId: string, signal?: AbortSignal) {
    if (!roundIdPattern.test(roundId)) throw new AndarBaharGatewayError('This deal reference is invalid.', 'andar-bahar-invalid-request')
    const round = await this.request(`/rounds/${encodeURIComponent(roundId)}`, { signal }, isRound)
    if (round.roundId !== roundId) throw new AndarBaharGatewayError(invalidMessage, 'andar-bahar-invalid-response')
    return round
  }

  async createRound(side: AndarBaharSide, stake: number, options: AndarBaharRequestOptions) {
    if (!isSide(side) || !isStake(stake) || !idempotencyKeyPattern.test(options.idempotencyKey)) {
      throw new AndarBaharGatewayError('Select a side and a valid wager before dealing.', 'andar-bahar-invalid-request')
    }
    const round = await this.request('/rounds', {
      method: 'POST', signal: options.signal,
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': options.idempotencyKey },
      body: JSON.stringify({ side, stake }),
    }, isRound)
    if (round.side !== side || cents(round.stake) !== cents(stake)) {
      throw new AndarBaharGatewayError('The table did not confirm this wager. Restore this deal before playing again.', 'andar-bahar-invalid-response')
    }
    return round
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T, allowEmpty = false): Promise<T> {
    const request = this.requestFn
    // Extracting fetch preserves its native receiver. A response cannot be cached across wallets.
    const response = await request(`${this.basePath.replace(/\/$/, '')}${path}`, { ...init, credentials: 'include', cache: 'no-store' })
    const value: unknown = response.status === 204 && allowEmpty ? null : await response.json().catch(() => undefined)
    if (!response.ok) {
      const error = record(value) ? value : {}
      throw new AndarBaharGatewayError(
        typeof error.message === 'string' && error.message.length <= 512 ? error.message : `The table request failed (${response.status}).`,
        typeof error.code === 'string' && error.code.length <= 128 ? error.code : undefined,
        response.status,
      )
    }
    // Only HTTP 204 means an absent previous round; 200 null is malformed.
    if (value === null && response.status !== 204 || !validate(value)) {
      throw new AndarBaharGatewayError(invalidMessage, 'andar-bahar-invalid-response')
    }
    return value
  }
}

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const exactKeys = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key))
const cents = (value: number) => Math.round(value * 100)
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 && Number.isSafeInteger(cents(value)) && Math.abs(value * 100 - cents(value)) < .000001
const signedMoney = (value: unknown): value is number => typeof value === 'number' && money(Math.abs(value))
const isSide = (value: unknown): value is AndarBaharSide => value === 'andar' || value === 'bahar'
const isStake = (value: unknown): value is number => money(value) && value >= .5 && value <= 100 && cents(value) % 50 === 0
const ranks = ['ace', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'jack', 'queen', 'king']
const suits = ['clubs', 'diamonds', 'hearts', 'spades']
const isCard = (value: unknown): value is AndarBaharCard => record(value) && exactKeys(value, ['rank', 'suit']) && typeof value.rank === 'string' && ranks.includes(value.rank) && typeof value.suit === 'string' && suits.includes(value.suit)
const cardKey = (card: AndarBaharCard) => `${card.rank}:${card.suit}`

function isStatus(value: unknown): value is AndarBaharStatus {
  return record(value) && exactKeys(value, ['available', 'minimumStake', 'maximumStake', 'stakeIncrement', 'balance', 'mode']) &&
    typeof value.available === 'boolean' && money(value.balance) && isStake(value.minimumStake) && isStake(value.maximumStake) &&
    value.maximumStake >= value.minimumStake && value.stakeIncrement === .5 &&
    (value.mode === 'development-practice-andar-first' || value.mode === 'andar-first')
}

/** Validate settlement for the documented single-deck, always-Andar-first variant. */
export function isRound(value: unknown): value is AndarBaharRound {
  if (!record(value) || !exactKeys(value, ['roundId', 'balance', 'phase', 'joker', 'deals', 'winner', 'side', 'stake', 'disposition', 'profit', 'totalReturn']) ||
    typeof value.roundId !== 'string' || !roundIdPattern.test(value.roundId) || !money(value.balance) || value.phase !== 'settled' ||
    !isCard(value.joker) || !isSide(value.side) || !isSide(value.winner) || !isStake(value.stake) ||
    !signedMoney(value.profit) || !money(value.totalReturn) || !Array.isArray(value.deals) || value.deals.length < 1 || value.deals.length > 49) return false

  const seen = new Set([cardKey(value.joker)])
  for (let index = 0; index < value.deals.length; index++) {
    const deal: unknown = value.deals[index]
    const expectedSide = index % 2 === 0 ? 'andar' : 'bahar'
    if (!record(deal) || !exactKeys(deal, ['number', 'side', 'card']) || deal.number !== index + 1 || deal.side !== expectedSide || !isCard(deal.card)) return false
    const key = cardKey(deal.card)
    if (seen.has(key)) return false
    seen.add(key)
    const isLast = index === value.deals.length - 1
    if ((deal.card.rank === value.joker.rank) !== isLast || isLast && deal.side !== value.winner) return false
  }

  const won = value.side === value.winner
  const expectedReturn = won ? cents(value.stake) * (value.side === 'andar' ? 19 : 20) / 10 : 0
  return value.disposition === (won ? 'win' : 'loss') && cents(value.totalReturn) === expectedReturn &&
    cents(value.profit) === expectedReturn - cents(value.stake)
}

import {
  SicBoGatewayError,
  type SicBoBetKind,
  type SicBoBetRequest,
  type SicBoGateway,
  type SicBoRound,
  type SicBoRequestOptions,
  type SicBoSettlement,
  type SicBoStatus,
} from './contracts'

export class HttpSicBoGateway implements SicBoGateway {
  readonly basePath: string
  private readonly requestFn: typeof fetch

  constructor(basePath = '/api/games/sic-bo', requestFn: typeof fetch = (...args) => globalThis.fetch(...args)) {
    this.basePath = basePath.replace(/\/$/, '')
    this.requestFn = requestFn
  }

  getStatus(signal?: AbortSignal) {
    return this.request('/status', { signal }, isStatus)
  }

  getRound(roundId: string, signal?: AbortSignal) {
    if (!isRoundId(roundId))
      throw new SicBoGatewayError('The Sic Bo round identifier is invalid.', 'sic-bo-invalid-request')
    return this.request(`/rounds/${roundId}`, { signal }, (value): value is SicBoRound =>
      isRound(value) && value.roundId === roundId)
  }

  createRound(bets: readonly SicBoBetRequest[], options: SicBoRequestOptions = {}) {
    if (!Array.isArray(bets) || bets.length === 0 || !Array.from(bets).every(isBetRequest))
      throw new SicBoGatewayError('The Sic Bo bet request is invalid.', 'sic-bo-invalid-request')
    const key = options.idempotencyKey ?? createIdempotencyKey()
    if (!isIdempotencyKey(key))
      throw new SicBoGatewayError('The Sic Bo request key is invalid.', 'sic-bo-invalid-request')
    const submitted = bets.map(bet => ({ ...bet }))
    return this.request('/rounds', jsonPost({ bets: submitted }, options.signal, key), value => isRound(value, submitted))
  }

  private async request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
    let response: Response
    try { response = await this.requestFn(`${this.basePath}${path}`, init) }
    catch (error) {
      if (isAbortError(error)) throw error
      throw new SicBoGatewayError(error instanceof Error ? error.message : 'The Sic Bo request could not be completed.')
    }

    const value: unknown = await response.json().catch(error => {
      if (isAbortError(error)) throw error
      return null
    })
    if (!response.ok) {
      const error = isError(value) ? value : null
      throw new SicBoGatewayError(error?.message ?? `The Sic Bo request failed (${response.status}).`, error?.code, response.status)
    }
    if (!validate(value)) throw new SicBoGatewayError('The Sic Bo service returned an invalid response.')
    return value
  }
}

function jsonPost(body: object, signal: AbortSignal | undefined, idempotencyKey: string): RequestInit {
  return { method: 'POST', headers: { 'content-type': 'application/json', 'Idempotency-Key': idempotencyKey }, body: JSON.stringify(body), signal }
}

function isStatus(value: unknown): value is SicBoStatus {
  return hasKeys(value, statusKeys) && typeof value.available === 'boolean' && isPositiveFinite(value.minimumStake) &&
    isPositiveFinite(value.maximumStakePerBet) && isPositiveFinite(value.stakeIncrement) &&
    isPositiveInteger(value.maximumBetsPerRound) && isNonNegativeFinite(value.balance) &&
    value.minimumStake <= value.maximumStakePerBet && isNonBlankString(value.mode)
}

function isRound(value: unknown, submittedBets?: readonly SicBoBetRequest[]): value is SicBoRound {
  if (!hasKeys(value, roundKeys) || !isResponseRoundId(value.roundId) || !isNonNegativeFinite(value.balance) || value.phase !== 'settled' ||
    !isDice(value.dice) || !isTotal(value.total) || typeof value.isTriple !== 'boolean' || !isNonNegativeFinite(value.totalStaked) ||
    !isNonNegativeFinite(value.totalReturn) || !isFiniteNumber(value.profit) || !Array.isArray(value.settlements) ||
    value.settlements.length === 0 || (submittedBets && value.settlements.length !== submittedBets.length)) return false

  const diceTotal = value.dice[0] + value.dice[1] + value.dice[2]
  const diceTriple = value.dice[0] === value.dice[1] && value.dice[1] === value.dice[2]
  if (value.total !== diceTotal || value.isTriple !== diceTriple) return false
  if (!Array.from(value.settlements).every((settlement, index) => isSettlement(settlement, index, submittedBets?.[index]))) return false

  const totalStaked = value.settlements.reduce((sum, settlement) => sum + settlement.stake, 0)
  const totalReturn = value.settlements.reduce((sum, settlement) => sum + settlement.totalReturn, 0)
  const profit = value.settlements.reduce((sum, settlement) => sum + settlement.profit, 0)
  return nearlyEqual(value.totalStaked, totalStaked) && nearlyEqual(value.totalReturn, totalReturn) && nearlyEqual(value.profit, profit) &&
    nearlyEqual(value.profit, value.totalReturn - value.totalStaked)
}

function isSettlement(value: unknown, expectedIndex: number, expectedBet?: SicBoBetRequest): value is SicBoSettlement {
  if (!hasKeys(value, settlementKeys) || value.betIndex !== expectedIndex ||
    typeof value.won !== 'boolean' || !isNonNegativeFinite(value.profitOdds) || !isFiniteNumber(value.profit) ||
    !isNonNegativeFinite(value.totalReturn)) return false
  if (!isBetShape(value) || (expectedBet && !sameBet(value, expectedBet))) return false
  const settlement = value as SicBoSettlement
  if (!settlement.won) return settlement.profitOdds === 0 && nearlyEqual(settlement.profit, -settlement.stake) && settlement.totalReturn === 0
  return settlement.profitOdds > 0 && nearlyEqual(settlement.profit, settlement.stake * settlement.profitOdds) &&
    nearlyEqual(settlement.totalReturn, settlement.stake * (settlement.profitOdds + 1))
}

function sameBet(actual: SicBoBetRequest, expected: SicBoBetRequest): boolean {
  return actual.kind === expected.kind && actual.stake === expected.stake && actual.face === expected.face &&
    actual.total === expected.total && actual.firstFace === expected.firstFace && actual.secondFace === expected.secondFace
}

function isBetRequest(value: unknown): value is SicBoBetRequest {
  return hasKeys(value, betKeys) && isBetShape(value)
}

function isBetShape(value: unknown): value is SicBoBetRequest {
  if (!isRecord(value) || !isBetKind(value.kind) || !isPositiveFinite(value.stake) || !isNullableFace(value.face) ||
    !isNullableBetTotal(value.total) || !isNullableFace(value.firstFace) || !isNullableFace(value.secondFace)) return false
  switch (value.kind) {
    case 'small': case 'big': case 'odd': case 'even': case 'any-triple':
      return value.face === null && value.total === null && value.firstFace === null && value.secondFace === null
    case 'single-number': case 'specific-double': case 'specific-triple':
      return value.face !== null && value.total === null && value.firstFace === null && value.secondFace === null
    case 'total':
      return value.face === null && value.total !== null && value.firstFace === null && value.secondFace === null
    case 'two-number-combination':
      return value.face === null && value.total === null && value.firstFace !== null && value.secondFace !== null && value.firstFace < value.secondFace
  }
}

function isDice(value: unknown): value is SicBoRound['dice'] {
  return Array.isArray(value) && value.length === 3 && Array.from(value).every(isFace)
}

function isFace(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 6
}

function isNullableFace(value: unknown): value is number | null { return value === null || isFace(value) }
function isNullableBetTotal(value: unknown): value is number | null { return value === null || isBetTotal(value) }
function isTotal(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 3 && value <= 18 }
function isBetTotal(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 4 && value <= 17 }
function isBetKind(value: unknown): value is SicBoBetKind { return value === 'small' || value === 'big' || value === 'odd' || value === 'even' || value === 'single-number' || value === 'total' || value === 'two-number-combination' || value === 'specific-double' || value === 'any-triple' || value === 'specific-triple' }
const betKeys = ['kind', 'stake', 'face', 'total', 'firstFace', 'secondFace'] as const
const settlementKeys = [...betKeys, 'betIndex', 'won', 'profitOdds', 'profit', 'totalReturn'] as const
const roundKeys = ['roundId', 'balance', 'phase', 'dice', 'total', 'isTriple', 'totalStaked', 'totalReturn', 'profit', 'settlements'] as const
const statusKeys = ['available', 'minimumStake', 'maximumStakePerBet', 'stakeIncrement', 'maximumBetsPerRound', 'balance', 'mode'] as const
function hasKeys(value: unknown, keys: readonly string[]): value is Record<string, unknown> {
  return isRecord(value) && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key))
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function isRoundId(value: unknown): value is string { return typeof value === 'string' && value.length === 64 && !/[^0-9a-f]/i.test(value) }
function isResponseRoundId(value: unknown): value is string { return isSafeIdentifier(value, 1, 128) }
function isIdempotencyKey(value: unknown): value is string { return isSafeIdentifier(value, 16, 128) }
function isSafeIdentifier(value: unknown, minimum: number, maximum: number): value is string {
  return typeof value === 'string' && value.length >= minimum && value.length <= maximum && !/[^a-z0-9_-]/i.test(value)
}
function isAbortError(value: unknown): boolean { return isRecord(value) && value.name === 'AbortError' }
function isNonBlankString(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0 }
function isPositiveFinite(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value > 0 }
function isNonNegativeFinite(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function isPositiveInteger(value: unknown): value is number { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 }
function isFiniteNumber(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) }
function nearlyEqual(left: number, right: number): boolean { return Number.isFinite(left) && Number.isFinite(right) && Math.abs(left - right) <= Number.EPSILON * Math.max(1, Math.abs(left), Math.abs(right)) * 16 }
function isError(value: unknown): value is { code: string; message: string } { return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string' }
function createIdempotencyKey(): string {
  const random = typeof globalThis.crypto?.randomUUID === 'function'
    ? globalThis.crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `sic-bo-${random}`
}

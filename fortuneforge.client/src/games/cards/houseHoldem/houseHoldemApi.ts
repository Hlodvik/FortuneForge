import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'
import type { CardRank, CardSuit, PlayingCardModel } from '../shared/cards'

const basePath = '/api/games/house-holdem'

export type HouseHoldemAction = 'check' | 'play-3x' | 'play-4x' | 'play-2x' | 'play-1x' | 'fold'
export type HouseHoldemPhase = 'preflop' | 'flop' | 'river' | 'settled'
export type HouseHoldemCard = Readonly<{ rank: string; suit: CardSuit }>
export type HouseHoldemStatus = Readonly<{
  available: boolean
  balance: number
  activeRoundId: string | null
  minimumAnte: number
  maximumAnte: number
  wagerIncrement: number
  maximumTrips: number
}>
export type HouseHoldemRound = Readonly<{
  roundId: string
  balance: number
  phase: HouseHoldemPhase
  ante: number
  blind: number
  trips: number
  play: number
  totalWager: number
  payout: number
  playerCards: readonly HouseHoldemCard[]
  dealerCards: readonly HouseHoldemCard[] | null
  board: readonly HouseHoldemCard[]
  playerHand: string | null
  dealerHand: string | null
  result: string | null
  allowedActions: readonly HouseHoldemAction[]
  canAdvance: boolean
  payoutBreakdown: Readonly<{ ante: number; blind: number; trips: number; play: number }> | null
}>

type Problem = Readonly<{ code?: string; error?: string; detail?: string; title?: string; message?: string }>

export class HouseHoldemRequestError extends Error {
  readonly status: number
  readonly code?: string
  constructor(message: string, status: number, code?: string) {
    super(message)
    this.name = 'HouseHoldemRequestError'
    this.status = status
    this.code = code
  }
}

export function houseHoldemMutationKey(): string {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `house_holdem_${Date.now()}_${Math.random().toString(36).slice(2, 14)}`
}

export function isUncertainHouseHoldemFailure(error: unknown): boolean {
  return !(error instanceof HouseHoldemRequestError) || error.status >= 500
}

export function getHouseHoldemStatus(signal?: AbortSignal): Promise<HouseHoldemStatus> {
  return request(`${basePath}/status`, { method: 'GET', cache: 'no-store', signal }, isStatus)
}

export function getHouseHoldemRound(roundId: string, signal?: AbortSignal): Promise<HouseHoldemRound> {
  return request(`${basePath}/rounds/${encodeURIComponent(roundId)}`, { method: 'GET', cache: 'no-store', signal }, isRound)
}

export function startHouseHoldemRound(ante: number, trips: number, idempotencyKey: string): Promise<HouseHoldemRound> {
  return request(`${basePath}/rounds`, post({ ante, trips }, idempotencyKey), isRound)
}

export function actHouseHoldemRound(roundId: string, action: HouseHoldemAction, idempotencyKey: string): Promise<HouseHoldemRound> {
  return request(`${basePath}/rounds/${encodeURIComponent(roundId)}/actions`, post({ action }, idempotencyKey), isRound)
}

export function advanceHouseHoldemRound(roundId: string, idempotencyKey: string): Promise<HouseHoldemRound> {
  return request(`${basePath}/rounds/${encodeURIComponent(roundId)}/advance`, post({}, idempotencyKey), isRound)
}

export function validHouseHoldemAnte(amount: number, status: HouseHoldemStatus): boolean {
  if (!Number.isFinite(amount) || amount < status.minimumAnte || amount > status.maximumAnte) return false
  const steps = (amount - status.minimumAnte) / status.wagerIncrement
  return Math.abs(steps - Math.round(steps)) < .000001
}

export function validHouseHoldemTrips(amount: number, status: HouseHoldemStatus): boolean {
  if (amount === 0) return true
  if (!Number.isFinite(amount) || amount < status.minimumAnte || amount > status.maximumTrips) return false
  const steps = (amount - status.minimumAnte) / status.wagerIncrement
  return Math.abs(steps - Math.round(steps)) < .000001
}

export function minimumHouseHoldemBankroll(ante: number, trips: number): number {
  // Ante + matching Blind are debited at deal; a final 1× Play is still mandatory unless the player folds.
  return Math.round((ante * 3 + trips) * 100) / 100
}

export function affordableHouseHoldemAction(round: HouseHoldemRound, action: HouseHoldemAction): boolean {
  if (!round.allowedActions.includes(action)) return false
  const multiplier = action.startsWith('play-') ? Number(action.slice(-2, -1)) : 0
  return round.balance * 100 + .000001 >= round.ante * multiplier * 100
}

export function toHouseHoldemPlayingCard(card: HouseHoldemCard, scope: string, index: number): PlayingCardModel {
  return { id: `${scope}-${index}-${card.rank}-${card.suit}`, rank: rankValue(card.rank)!, suit: card.suit }
}

function post(body: Record<string, unknown>, idempotencyKey: string): RequestInit {
  return { method: 'POST', headers: new Headers({ 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }), body: JSON.stringify(body) }
}

async function request<T>(path: string, init: RequestInit, validate: (value: unknown) => value is T): Promise<T> {
  const response = await fetchWithAccountSession(path, init)
  const body: unknown = await response.json().catch(() => null)
  if (!response.ok) {
    const problem = isRecord(body) ? body as Problem : null
    throw new HouseHoldemRequestError(problem?.message ?? problem?.detail ?? problem?.error ?? problem?.title ?? `House Hold'em request failed (${response.status}).`, response.status, problem?.code)
  }
  if (!validate(body)) throw new Error('The House Hold’em server returned an invalid response.')
  return body
}

function isStatus(value: unknown): value is HouseHoldemStatus {
  return isRecord(value) && typeof value.available === 'boolean' && nonNegative(value.balance) && nullableString(value.activeRoundId)
    && positive(value.minimumAnte) && positive(value.maximumAnte) && positive(value.wagerIncrement)
    && nonNegative(value.maximumTrips) && value.maximumAnte >= value.minimumAnte
}

function isRound(value: unknown): value is HouseHoldemRound {
  if (!isRecord(value) || typeof value.roundId !== 'string' || !['preflop', 'flop', 'river', 'settled'].includes(String(value.phase))) return false
  if (!['balance', 'ante', 'blind', 'trips', 'play', 'totalWager', 'payout'].every((key) => nonNegative(value[key]))) return false
  if (!isCards(value.playerCards, 2) || value.playerCards.length !== 2) return false
  if (value.dealerCards !== null && (!isCards(value.dealerCards, 2) || value.dealerCards.length !== 2)) return false
  if (!isCards(value.board, 5)) return false
  if (value.phase === 'preflop' && value.board.length !== 0) return false
  if (value.phase === 'flop' && value.board.length !== 3) return false
  if ((value.phase === 'river' || value.phase === 'settled') && value.board.length !== 5) return false
  if (value.phase !== 'settled' && (value.dealerCards !== null || value.dealerHand !== null || value.result !== null || value.payoutBreakdown !== null)) return false
  if (!nullableString(value.playerHand) || !nullableString(value.dealerHand) || !nullableString(value.result)) return false
  if (!Array.isArray(value.allowedActions) || !value.allowedActions.every((action) => ['check', 'play-3x', 'play-4x', 'play-2x', 'play-1x', 'fold'].includes(action))) return false
  if (typeof value.canAdvance !== 'boolean') return false
  return value.payoutBreakdown === null || (isRecord(value.payoutBreakdown) && ['ante', 'blind', 'trips', 'play'].every((key) => nonNegative((value.payoutBreakdown as Record<string, unknown>)[key])))
}

function isCards(value: unknown, max: number): value is readonly HouseHoldemCard[] {
  return Array.isArray(value) && value.length <= max && value.every((card) => isRecord(card) && typeof card.rank === 'string'
    && rankValue(card.rank) !== null && ['clubs', 'diamonds', 'hearts', 'spades'].includes(String(card.suit)))
}

function rankValue(rank: string): CardRank | null {
  const normalized = rank.trim().toLowerCase()
  const words: Record<string, CardRank> = {
    two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10,
  }
  if (normalized in words) return words[normalized] ?? null
  if (normalized === 'ace' || normalized === 'a') return 1
  if (normalized === 'jack' || normalized === 'j') return 11
  if (normalized === 'queen' || normalized === 'q') return 12
  if (normalized === 'king' || normalized === 'k') return 13
  const value = Number(normalized)
  return Number.isInteger(value) && value >= 2 && value <= 10 ? value as CardRank : null
}

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function nonNegative(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function positive(value: unknown): value is number { return nonNegative(value) && value > 0 }
function nullableString(value: unknown): boolean { return value === null || typeof value === 'string' }

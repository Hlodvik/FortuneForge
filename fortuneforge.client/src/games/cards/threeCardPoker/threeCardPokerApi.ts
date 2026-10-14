import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'
import type { CardRank, CardSuit, PlayingCardModel } from '../shared/cards'

const basePath = '/api/cards/three-card-poker/table'
const contractVersion = 'cards.three-card-poker.table.v1'

export type ThreeCardPokerAction = 'play' | 'fold'
export type ThreeCardPokerStatus = Readonly<{
  available: boolean
  minimumWager: number
  maximumWager: number
  wagerIncrement: number
  minimumStartOccupancy: number
  tableCapacity: number
  humanGraceSeconds: number
  actionDeadlineSeconds: number
  dealerRule: string
  pairPlusAllowed: boolean
  anteBonusPaytable: string
  pairPlusPaytable: string
  pairPlusPaysOnFold: boolean
}>
export type ThreeCardPokerCard = Readonly<{ rank?: string | null; suit?: CardSuit | null; hidden: boolean }>
export type ThreeCardPokerHand = Readonly<{ cards: readonly ThreeCardPokerCard[]; rank: string | null }>
export type ThreeCardPokerSeat = Readonly<{
  seatId: string
  displayName: string
  seat: number
  status: string
  ante: number
  pairPlus: number
  playWager: number
  totalWager: number
  payout: number
  anteBonusPayout: number
  pairPlusPayout: number
  outcome?: string | null
  lastAction?: string | null
  hand: ThreeCardPokerHand
  isCurrentPlayer: boolean
}>
export type ThreeCardPokerTable = Readonly<{
  tableId: string
  phase: string
  round: number
  dealer: ThreeCardPokerHand
  seats: readonly ThreeCardPokerSeat[]
  activeSeat: number | null
  legalActions: readonly ThreeCardPokerAction[]
  createdAtUtc: string
  updatedAtUtc: string
  actionDeadlineAtUtc: string | null
  wagerDeadlineAtUtc: string | null
  transition: string | null
  nextTransitionAtUtc: string | null
  remainingActionMilliseconds: number
  remainingWagerMilliseconds: number
  remainingTransitionMilliseconds: number
}>
type SessionBase = Readonly<{ contractVersion: typeof contractVersion; version: number }>
export type ThreeCardPokerQueueSession = SessionBase & Readonly<{
  kind: 'queue'; ticketId: string; position: number; joinedAtUtc: string
  humanGraceEndsAtUtc: string; players: readonly ThreeCardPokerSeat[]
}>
export type ThreeCardPokerPlaySession = SessionBase & Readonly<{ kind: 'table'; table: ThreeCardPokerTable }>
export type ThreeCardPokerSession = (SessionBase & Readonly<{ kind: 'idle' }>) | ThreeCardPokerQueueSession | ThreeCardPokerPlaySession
export type ThreeCardPokerMutationResponse = Readonly<{ session: ThreeCardPokerSession; balanceCredits: number }>
export type PendingThreeCardPokerMutation = Readonly<{ fingerprint: string; idempotencyKey: string }>

type Problem = Readonly<{ code?: string; error?: string; detail?: string }>
export class ThreeCardPokerRequestError extends Error {
  readonly status: number
  readonly code?: string
  constructor(message: string, status: number, problem: Problem | null) {
    super(message)
    this.name = 'ThreeCardPokerRequestError'
    this.status = status
    this.code = problem?.code
  }
}

export function stableThreeCardPokerMutation(current: PendingThreeCardPokerMutation | null, fingerprint: string): PendingThreeCardPokerMutation {
  return current?.fingerprint === fingerprint ? current : {
    fingerprint,
    idempotencyKey: typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : `three_card_poker_${Date.now()}_${Math.random().toString(36).slice(2, 14)}`,
  }
}
export function isUncertainThreeCardPokerFailure(error: unknown): boolean {
  return !(error instanceof ThreeCardPokerRequestError) || error.status >= 500
}
export async function getThreeCardPokerStatus(signal?: AbortSignal): Promise<ThreeCardPokerStatus> {
  return readResponse(await fetchWithAccountSession(`${basePath}/status`, { method: 'GET', cache: 'no-store', signal }), isStatus, 'status')
}
export async function getThreeCardPokerSession(signal?: AbortSignal): Promise<ThreeCardPokerSession> {
  return readResponse(await fetchWithAccountSession(`${basePath}/session`, { method: 'GET', cache: 'no-store', signal }), isSession, 'session')
}
export async function getThreeCardPokerSnapshot(signal?: AbortSignal): Promise<ThreeCardPokerMutationResponse> {
  return readResponse(await fetchWithAccountSession(`${basePath}/snapshot`, { method: 'GET', cache: 'no-store', signal }), isMutation, 'snapshot')
}
export function joinThreeCardPokerQueue(expectedVersion: number, key: string): Promise<ThreeCardPokerMutationResponse> {
  return mutation('/queue', 'POST', { expectedVersion }, key)
}
export function cancelThreeCardPokerQueue(ticketId: string, expectedVersion: number, key: string): Promise<ThreeCardPokerMutationResponse> {
  return mutation(`/queue/${encodeURIComponent(ticketId)}`, 'DELETE', { expectedVersion }, key)
}
export function postThreeCardPokerWager(tableId: string, ante: number, pairPlus: number, expectedVersion: number, key: string): Promise<ThreeCardPokerMutationResponse> {
  return mutation(`/tables/${encodeURIComponent(tableId)}/wagers`, 'POST', { ante, pairPlus, expectedVersion }, key)
}
export function postThreeCardPokerAction(tableId: string, type: ThreeCardPokerAction, expectedVersion: number, key: string): Promise<ThreeCardPokerMutationResponse> {
  return mutation(`/tables/${encodeURIComponent(tableId)}/actions`, 'POST', { type, expectedVersion }, key)
}
export function sitOutThreeCardPokerRound(tableId: string, expectedVersion: number, key: string): Promise<ThreeCardPokerMutationResponse> {
  return mutation(`/tables/${encodeURIComponent(tableId)}/sit-out`, 'POST', { expectedVersion }, key)
}
export function leaveThreeCardPokerTable(tableId: string, expectedVersion: number, key: string): Promise<ThreeCardPokerMutationResponse> {
  return mutation(`/tables/${encodeURIComponent(tableId)}/leave`, 'POST', { expectedVersion }, key)
}
export function toThreeCardPokerPlayingCard(card: ThreeCardPokerCard, index: number, scope: string): PlayingCardModel | null {
  if (card.hidden || !card.rank || !card.suit) return null
  const rank = rankValue(card.rank)
  return rank === null ? null : { id: `${scope}-${index}-${card.rank}-${card.suit}`, rank, suit: card.suit }
}
export function validThreeCardPokerWager(value: number, status: ThreeCardPokerStatus, optional = false): boolean {
  if (optional && value === 0) return true
  if (!Number.isFinite(value) || value < status.minimumWager || value > status.maximumWager) return false
  const increments = (value - status.minimumWager) / status.wagerIncrement
  return Math.abs(increments - Math.round(increments)) < .000001
}

async function mutation(suffix: string, method: 'POST' | 'DELETE', body: Record<string, unknown>, key: string): Promise<ThreeCardPokerMutationResponse> {
  return readResponse(await fetchWithAccountSession(`${basePath}${suffix}`, {
    method, headers: new Headers({ 'Content-Type': 'application/json', 'Idempotency-Key': key }), body: JSON.stringify(body),
  }), isMutation, 'mutation')
}
async function readResponse<T>(response: Response, validator: (value: unknown) => value is T, label: string): Promise<T> {
  const value: unknown = await response.json().catch(() => null)
  if (!response.ok) {
    const problem = isRecord(value) ? value as Problem : null
    throw new ThreeCardPokerRequestError(problem?.error ?? problem?.detail ?? `Three Card Poker ${label} request failed (${response.status}).`, response.status, problem)
  }
  if (hasPrivateField(value) || !validator(value)) throw new Error(`The server returned an invalid Three Card Poker ${label} response.`)
  return value
}
function hasPrivateField(value: unknown): boolean {
  if (Array.isArray(value)) return value.some(hasPrivateField)
  if (!isRecord(value)) return typeof value === 'string' && /^(?:bot|actor|user):/i.test(value)
  return Object.entries(value).some(([key, child]) => {
    const name = key.replace(/[^a-z0-9]/gi, '').toLowerCase()
    return name.includes('bot') || name.includes('skill') || name.includes('seed') || ['actorid', 'userid', 'accountid', 'deck'].includes(name) || name.startsWith('raw') || hasPrivateField(child)
  })
}
function isStatus(value: unknown): value is ThreeCardPokerStatus {
  return isRecord(value) && typeof value.available === 'boolean'
    && positive(value.minimumWager) && positive(value.maximumWager) && positive(value.wagerIncrement)
    && ((value.minimumStartOccupancy === 1 && value.tableCapacity === 1 && nonNegativeInteger(value.humanGraceSeconds))
      || (value.minimumStartOccupancy === 3 && value.tableCapacity === 5 && positiveInteger(value.humanGraceSeconds)))
    && nonNegativeInteger(value.actionDeadlineSeconds)
    && typeof value.dealerRule === 'string' && typeof value.pairPlusAllowed === 'boolean'
    && typeof value.anteBonusPaytable === 'string' && typeof value.pairPlusPaytable === 'string' && typeof value.pairPlusPaysOnFold === 'boolean'
}
function isMutation(value: unknown): value is ThreeCardPokerMutationResponse {
  return isRecord(value) && isSession(value.session) && nonNegative(value.balanceCredits)
}
function isSession(value: unknown): value is ThreeCardPokerSession {
  if (!isRecord(value) || value.contractVersion !== contractVersion || !nonNegativeInteger(value.version)) return false
  if (value.kind === 'idle') return true
  if (value.kind === 'queue') return typeof value.ticketId === 'string' && positiveInteger(value.position)
    && typeof value.joinedAtUtc === 'string' && typeof value.humanGraceEndsAtUtc === 'string' && isSeats(value.players)
  return value.kind === 'table' && isTable(value.table)
}
function isTable(value: unknown): value is ThreeCardPokerTable {
  return isRecord(value) && typeof value.tableId === 'string' && typeof value.phase === 'string' && nonNegativeInteger(value.round)
    && isHand(value.dealer) && isSeats(value.seats) && (value.activeSeat === null || Number.isInteger(value.activeSeat))
    && Array.isArray(value.legalActions) && value.legalActions.every((action) => action === 'play' || action === 'fold')
    && typeof value.createdAtUtc === 'string' && typeof value.updatedAtUtc === 'string'
    && nullableString(value.actionDeadlineAtUtc) && nullableString(value.wagerDeadlineAtUtc)
    && nullableString(value.transition) && nullableString(value.nextTransitionAtUtc)
    && nonNegative(value.remainingActionMilliseconds) && nonNegative(value.remainingWagerMilliseconds) && nonNegative(value.remainingTransitionMilliseconds)
}
function isSeats(value: unknown): value is readonly ThreeCardPokerSeat[] {
  return Array.isArray(value) && value.length <= 5 && value.every((seat) => isRecord(seat)
    && typeof seat.seatId === 'string' && typeof seat.displayName === 'string' && nonNegativeInteger(seat.seat) && Number(seat.seat) < 5
    && typeof seat.status === 'string' && ['ante', 'pairPlus', 'playWager', 'totalWager', 'payout', 'anteBonusPayout', 'pairPlusPayout'].every((key) => nonNegative(seat[key]))
    && (seat.outcome == null || typeof seat.outcome === 'string') && (seat.lastAction == null || typeof seat.lastAction === 'string')
    && isHand(seat.hand) && typeof seat.isCurrentPlayer === 'boolean')
}
function isHand(value: unknown): value is ThreeCardPokerHand {
  return isRecord(value) && Array.isArray(value.cards) && value.cards.length <= 3 && value.cards.every((card) => isRecord(card)
    && typeof card.hidden === 'boolean' && (card.hidden ? card.rank == null && card.suit == null
      : typeof card.rank === 'string' && rankValue(card.rank) !== null && ['clubs', 'diamonds', 'hearts', 'spades'].includes(String(card.suit))))
    && nullableString(value.rank)
}
function rankValue(rank: string): CardRank | null {
  if (rank === 'A') return 1
  if (rank === 'J') return 11
  if (rank === 'Q') return 12
  if (rank === 'K') return 13
  const value = Number(rank)
  return Number.isInteger(value) && value >= 2 && value <= 10 ? value as CardRank : null
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function nonNegative(value: unknown): value is number { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function positive(value: unknown): value is number { return nonNegative(value) && value > 0 }
function positiveInteger(value: unknown): value is number { return positive(value) && Number.isInteger(value) }
function nonNegativeInteger(value: unknown): value is number { return nonNegative(value) && Number.isInteger(value) }
function nullableString(value: unknown): boolean { return value === null || typeof value === 'string' }

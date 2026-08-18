import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'
import type { CardRank, CardSuit, PlayingCardModel } from '../shared/cards'

const basePath = '/api/cards/blackjack/table'
const contractVersion = 'cards.blackjack.table.v2'

export type BlackjackTableAction =
  | 'hit'
  | 'stand'
  | 'double'
  | 'split'
  | 'surrender'
  | 'insurance'
  | 'decline-insurance'

export type BlackjackTableStatus = Readonly<{
  available: boolean
  minimumWager: number
  maximumWager: number
  wagerIncrement: number
  minimumStartOccupancy: number
  tableCapacity: number
  humanGraceSeconds: number
  actionDeadlineSeconds: number
  dealerRule: string
  blackjackPayout: string
  doubleAllowed: boolean
  splitAllowed: boolean
  insuranceAllowed: boolean
  surrenderAllowed?: boolean
}>

export type BlackjackTableCard = Readonly<{
  rank?: string | null
  suit?: CardSuit | null
  hidden: boolean
}>

export type BlackjackTableHand = Readonly<{
  cards: readonly BlackjackTableCard[]
  score: number | null
  soft: boolean
  blackjack: boolean
  bust: boolean
}>

export type BlackjackTablePlayerHand = Readonly<{
  handNumber: number
  hand: BlackjackTableHand
  wager: number
  totalWager: number
  payout: number
  status: string
  outcome?: string | null
  lastAction?: string | null
  active: boolean
}>

export type BlackjackTableSeat = Readonly<{
  seatId: string
  displayName: string
  seat: number
  status: string
  wager: number
  totalWager: number
  payout: number
  outcome?: string | null
  lastAction?: string | null
  hand: BlackjackTableHand
  isCurrentPlayer: boolean
  hands?: readonly BlackjackTablePlayerHand[]
  insuranceWager?: number
  insurancePayout?: number
}>

type BlackjackTableSessionBase = Readonly<{
  contractVersion: typeof contractVersion
  kind: 'idle' | 'queue' | 'table'
  version: number
}>

export type BlackjackTableIdleSession = BlackjackTableSessionBase & Readonly<{ kind: 'idle' }>

export type BlackjackTableQueueSession = BlackjackTableSessionBase & Readonly<{
  kind: 'queue'
  ticketId: string
  position: number
  joinedAtUtc: string
  humanGraceEndsAtUtc: string
  players: readonly BlackjackTableSeat[]
}>

export type BlackjackTable = Readonly<{
  tableId: string
  phase: string
  round: number
  dealer: BlackjackTableHand
  seats: readonly BlackjackTableSeat[]
  activeSeat: number | null
  legalActions: readonly BlackjackTableAction[]
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

export type BlackjackTablePlaySession = BlackjackTableSessionBase & Readonly<{
  kind: 'table'
  table: BlackjackTable
}>

export type BlackjackTableSession =
  | BlackjackTableIdleSession
  | BlackjackTableQueueSession
  | BlackjackTablePlaySession

export type BlackjackTableMutationResponse = Readonly<{
  session: BlackjackTableSession
  balanceCredits: number
}>

export type BlackjackTableHistoryItem = Readonly<{
  resultId: string
  game: 'blackjack'
  mode: 'credit-table'
  matchId: string
  tableId: string
  round: number
  wagerCredits: number
  payoutCredits: number
  netCredits: number
  claimStatus: 'completed'
  settlementStatus: 'paid'
  completedAtUtc: string
  seen: boolean
  seenAtUtc: string | null
}>

type BlackjackTableProblem = Readonly<{
  code?: string
  error?: string
  detail?: string
  available?: number
  required?: number
}>

export class BlackjackTableRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number

  constructor(message: string, status: number, problem: BlackjackTableProblem | null) {
    super(message)
    this.name = 'BlackjackTableRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
  }
}

export type PendingBlackjackTableMutation = Readonly<{
  fingerprint: string
  idempotencyKey: string
}>

export function createBlackjackTableRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `blackjack_table_${Date.now()}_${Math.random().toString(36).slice(2, 14)}`
}

export function stableBlackjackTableMutation(
  current: PendingBlackjackTableMutation | null,
  fingerprint: string,
): PendingBlackjackTableMutation {
  return current?.fingerprint === fingerprint
    ? current
    : { fingerprint, idempotencyKey: createBlackjackTableRequestId() }
}

export async function getBlackjackTableStatus(signal?: AbortSignal): Promise<BlackjackTableStatus> {
  const response = await fetchWithAccountSession(`${basePath}/status`, {
    method: 'GET', cache: 'no-store', signal,
  })
  return readResponse(response, isBlackjackTableStatus, 'Blackjack table status')
}

export async function getBlackjackTableSession(signal?: AbortSignal): Promise<BlackjackTableSession> {
  const response = await fetchWithAccountSession(`${basePath}/session`, {
    method: 'GET', cache: 'no-store', signal,
  })
  return readResponse(response, isBlackjackTableSession, 'Blackjack table session')
}

export async function getBlackjackTableHistory(
  limit = 20,
  signal?: AbortSignal,
): Promise<readonly BlackjackTableHistoryItem[]> {
  const response = await fetchWithAccountSession(`${basePath}/history?limit=${encodeURIComponent(limit)}`, {
    method: 'GET', cache: 'no-store', signal,
  })
  return readResponse(response, isBlackjackTableHistory, 'Blackjack table history')
}

export async function markBlackjackTableHistorySeen(
  resultId: string,
): Promise<BlackjackTableHistoryItem> {
  const response = await fetchWithAccountSession(
    `${basePath}/history/${encodeURIComponent(resultId)}/seen`,
    { method: 'POST' },
  )
  return readResponse(response, isBlackjackTableHistoryItem, 'Blackjack table history update')
}

export function joinBlackjackTableQueue(
  expectedVersion: number,
  idempotencyKey: string,
): Promise<BlackjackTableMutationResponse> {
  return mutationRequest('/queue', 'POST', { expectedVersion }, idempotencyKey)
}

export function cancelBlackjackTableQueue(
  ticketId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<BlackjackTableMutationResponse> {
  return mutationRequest(
    `/queue/${encodeURIComponent(ticketId)}`,
    'DELETE',
    { expectedVersion },
    idempotencyKey,
  )
}

export function postBlackjackTableWager(
  tableId: string,
  wager: number,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<BlackjackTableMutationResponse> {
  return mutationRequest(
    `/tables/${encodeURIComponent(tableId)}/wagers`,
    'POST',
    { wager, expectedVersion },
    idempotencyKey,
  )
}

export function postBlackjackTableAction(
  tableId: string,
  type: BlackjackTableAction,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<BlackjackTableMutationResponse> {
  return mutationRequest(
    `/tables/${encodeURIComponent(tableId)}/actions`,
    'POST',
    { type, expectedVersion },
    idempotencyKey,
  )
}

export function leaveBlackjackTable(
  tableId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<BlackjackTableMutationResponse> {
  return mutationRequest(
    `/tables/${encodeURIComponent(tableId)}/leave`,
    'POST',
    { expectedVersion },
    idempotencyKey,
  )
}

export function isUncertainBlackjackTableFailure(error: unknown): boolean {
  return !(error instanceof BlackjackTableRequestError) || error.status >= 500
}

export function toBlackjackTablePlayingCard(
  card: BlackjackTableCard,
  index: number,
  scope: string,
): PlayingCardModel | null {
  if (card.hidden || card.rank == null || card.suit == null) return null
  const rank = rankValue(card.rank)
  if (rank === null) return null
  return { id: `${scope}-${card.suit}-${card.rank}-${index}`, suit: card.suit, rank }
}

function mutationRequest(
  suffix: string,
  method: 'POST' | 'DELETE',
  body: Record<string, unknown>,
  idempotencyKey: string,
): Promise<BlackjackTableMutationResponse> {
  return fetchWithAccountSession(`${basePath}${suffix}`, {
    method,
    headers: new Headers({ 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }),
    body: JSON.stringify(body),
  }).then((response) => readResponse(response, isBlackjackTableMutation, 'Blackjack table mutation'))
}

async function readResponse<T>(
  response: Response,
  validator: (value: unknown) => value is T,
  label: string,
): Promise<T> {
  const value = await response.json().catch(() => null) as unknown
  if (!response.ok) {
    const problem = isRecord(value) ? value as BlackjackTableProblem : null
    throw new BlackjackTableRequestError(
      problem?.error ?? problem?.detail ?? `${label} request failed (${response.status}).`,
      response.status,
      problem,
    )
  }
  if (hasForbiddenPublicField(value) || !validator(value)) {
    throw new Error(`The server returned an invalid ${label.toLowerCase()} response.`)
  }
  return value
}

function hasForbiddenPublicField(value: unknown): boolean {
  if (Array.isArray(value)) return value.some(hasForbiddenPublicField)
  if (!isRecord(value)) {
    return typeof value === 'string' && /^(?:bot|actor|user):/i.test(value)
  }
  return Object.entries(value).some(([key, child]) => {
    const normalized = key.replace(/[^a-z0-9]/gi, '').toLowerCase()
    return normalized.includes('bot')
      || normalized.includes('skill')
      || normalized.includes('seed')
      || normalized === 'actorid'
      || normalized === 'userid'
      || normalized === 'accountid'
      || normalized === 'deck'
      || normalized.startsWith('raw')
      || hasForbiddenPublicField(child)
  })
}

function isBlackjackTableStatus(value: unknown): value is BlackjackTableStatus {
  return isRecord(value)
    && typeof value.available === 'boolean'
    && isPositiveNumber(value.minimumWager)
    && isPositiveNumber(value.maximumWager)
    && isPositiveNumber(value.wagerIncrement)
    && value.minimumStartOccupancy === 3
    && value.tableCapacity === 5
    && isPositiveInteger(value.humanGraceSeconds)
    && isPositiveInteger(value.actionDeadlineSeconds)
    && typeof value.dealerRule === 'string'
    && typeof value.blackjackPayout === 'string'
    && typeof value.doubleAllowed === 'boolean'
    && typeof value.splitAllowed === 'boolean'
    && typeof value.insuranceAllowed === 'boolean'
    && (value.surrenderAllowed === undefined || typeof value.surrenderAllowed === 'boolean')
}

function isBlackjackTableMutation(value: unknown): value is BlackjackTableMutationResponse {
  return isRecord(value)
    && isBlackjackTableSession(value.session)
    && isNonNegativeNumber(value.balanceCredits)
}

function isBlackjackTableHistory(value: unknown): value is readonly BlackjackTableHistoryItem[] {
  return Array.isArray(value) && value.length <= 50 && value.every(isBlackjackTableHistoryItem)
}

function isBlackjackTableHistoryItem(value: unknown): value is BlackjackTableHistoryItem {
  return isRecord(value)
    && typeof value.resultId === 'string'
    && value.game === 'blackjack'
    && value.mode === 'credit-table'
    && typeof value.matchId === 'string'
    && typeof value.tableId === 'string'
    && isPositiveInteger(value.round)
    && isNonNegativeNumber(value.wagerCredits)
    && isNonNegativeNumber(value.payoutCredits)
    && isFiniteNumber(value.netCredits)
    && value.claimStatus === 'completed'
    && value.settlementStatus === 'paid'
    && typeof value.completedAtUtc === 'string'
    && typeof value.seen === 'boolean'
    && (value.seenAtUtc === null || typeof value.seenAtUtc === 'string')
}

function isBlackjackTableSession(value: unknown): value is BlackjackTableSession {
  if (!isRecord(value)
    || value.contractVersion !== contractVersion
    || !isNonNegativeInteger(value.version)) return false
  if (value.kind === 'idle') return true
  if (value.kind === 'queue') {
    return typeof value.ticketId === 'string'
      && isPositiveInteger(value.position)
      && typeof value.joinedAtUtc === 'string'
      && typeof value.humanGraceEndsAtUtc === 'string'
      && isSeatList(value.players)
      && value.players.length <= 5
  }
  return value.kind === 'table' && isBlackjackTable(value.table)
}

function isBlackjackTable(value: unknown): value is BlackjackTable {
  return isRecord(value)
    && typeof value.tableId === 'string'
    && typeof value.phase === 'string'
    && isNonNegativeInteger(value.round)
    && isBlackjackTableHand(value.dealer)
    && isSeatList(value.seats)
    && value.seats.length <= 5
    && (value.activeSeat === null || Number.isInteger(value.activeSeat))
    && Array.isArray(value.legalActions)
    && value.legalActions.every(isBlackjackTableAction)
    && typeof value.createdAtUtc === 'string'
    && typeof value.updatedAtUtc === 'string'
    && (value.actionDeadlineAtUtc === null || typeof value.actionDeadlineAtUtc === 'string')
    && (value.wagerDeadlineAtUtc === null || typeof value.wagerDeadlineAtUtc === 'string')
    && (value.transition === null || typeof value.transition === 'string')
    && (value.nextTransitionAtUtc === null || typeof value.nextTransitionAtUtc === 'string')
    && isNonNegativeNumber(value.remainingActionMilliseconds)
    && isNonNegativeNumber(value.remainingWagerMilliseconds)
    && isNonNegativeNumber(value.remainingTransitionMilliseconds)
}

function isSeatList(value: unknown): value is readonly BlackjackTableSeat[] {
  return Array.isArray(value) && value.every(isBlackjackTableSeat)
}

function isBlackjackTableSeat(value: unknown): value is BlackjackTableSeat {
  return isRecord(value)
    && typeof value.seatId === 'string'
    && typeof value.displayName === 'string'
    && Number.isInteger(value.seat)
    && typeof value.status === 'string'
    && isNonNegativeNumber(value.wager)
    && isNonNegativeNumber(value.totalWager)
    && isNonNegativeNumber(value.payout)
    && (value.outcome == null || typeof value.outcome === 'string')
    && (value.lastAction == null || typeof value.lastAction === 'string')
    && isBlackjackTableHand(value.hand)
    && typeof value.isCurrentPlayer === 'boolean'
    && (value.hands === undefined || (Array.isArray(value.hands) && value.hands.length <= 2 && value.hands.every(isBlackjackTablePlayerHand)))
    && (value.insuranceWager === undefined || isNonNegativeNumber(value.insuranceWager))
    && (value.insurancePayout === undefined || isNonNegativeNumber(value.insurancePayout))
}

function isBlackjackTablePlayerHand(value: unknown): value is BlackjackTablePlayerHand {
  return isRecord(value)
    && isPositiveInteger(value.handNumber)
    && isBlackjackTableHand(value.hand)
    && isNonNegativeNumber(value.wager)
    && isNonNegativeNumber(value.totalWager)
    && isNonNegativeNumber(value.payout)
    && typeof value.status === 'string'
    && (value.outcome == null || typeof value.outcome === 'string')
    && (value.lastAction == null || typeof value.lastAction === 'string')
    && typeof value.active === 'boolean'
}

function isBlackjackTableHand(value: unknown): value is BlackjackTableHand {
  return isRecord(value)
    && Array.isArray(value.cards)
    && value.cards.every(isBlackjackTableCard)
    && (value.score === null || isNonNegativeInteger(value.score))
    && typeof value.soft === 'boolean'
    && typeof value.blackjack === 'boolean'
    && typeof value.bust === 'boolean'
}

function isBlackjackTableCard(value: unknown): value is BlackjackTableCard {
  if (!isRecord(value) || typeof value.hidden !== 'boolean') return false
  if (value.hidden) return value.rank == null && value.suit == null
  return typeof value.rank === 'string'
    && rankValue(value.rank) !== null
    && isCardSuit(value.suit)
}

function isBlackjackTableAction(value: unknown): value is BlackjackTableAction {
  return value === 'hit'
    || value === 'stand'
    || value === 'double'
    || value === 'split'
    || value === 'surrender'
    || value === 'insurance'
    || value === 'decline-insurance'
}

function isCardSuit(value: unknown): value is CardSuit {
  return value === 'clubs' || value === 'diamonds' || value === 'hearts' || value === 'spades'
}

function rankValue(rank: string): CardRank | null {
  if (rank === 'A') return 1
  if (rank === 'J') return 11
  if (rank === 'Q') return 12
  if (rank === 'K') return 13
  const value = Number(rank)
  return Number.isInteger(value) && value >= 2 && value <= 10 ? value as CardRank : null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isPositiveInteger(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) > 0
}

function isNonNegativeInteger(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) >= 0
}

function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function isNonNegativeNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'
import type { CardRank, CardSuit, PlayingCardModel } from '../shared/cards'

const basePath = '/api/cards/texas-holdem/credit'
const contractVersion = 'cards.texas-holdem.credit.v2'

export type CreditHoldemAction = 'fold' | 'check' | 'call' | 'raise'
export type CreditHoldemTableRule = Readonly<{
  id: string
  name: string
  description: string
  smallBlindCredits: number
  bigBlindCredits: number
  anteCredits: number
  maximumTableStackCredits: number
}>
export type CreditHoldemStatus = Readonly<{
  available: boolean
  minimumStartPlayers: number
  maximumSeats: number
  minimumRealPlayers: number
  smallBlindCredits: number
  bigBlindCredits: number
  actionDeadlineSeconds: number
  matchDeadlineSeconds: number
  tableRules?: readonly CreditHoldemTableRule[]
}>
export type CreditHoldemCard = Readonly<{ rank?: string | null; suit?: CardSuit | null; hidden: boolean }>
export type CreditHoldemSeat = Readonly<{
  seatId: string
  displayName: string
  seat: number
  startingStack: number
  stack: number
  committed: number
  committedRound: number
  status: string
  lastAction?: string | null
  holeCards: readonly CreditHoldemCard[]
  handName?: string | null
  isCurrentPlayer: boolean
}>
type CreditHoldemSessionBase = Readonly<{
  contractVersion: typeof contractVersion
  kind: 'idle' | 'queue' | 'match' | 'result'
  version: number
}>
export type CreditHoldemIdleSession = CreditHoldemSessionBase & Readonly<{ kind: 'idle' }>
export type CreditHoldemQueueSession = CreditHoldemSessionBase & Readonly<{
  kind: 'queue'
  ticketId: string
  position: number
  joinedAtUtc: string
  humanGraceEndsAtUtc: string
  players: readonly CreditHoldemSeat[]
  tableRule?: CreditHoldemTableRule | null
}>
export type CreditHoldemTable = Readonly<{
  matchId: string
  status: string
  street: string
  handNumber: number
  dealerSeat: number
  activeSeat: number
  pot: number
  currentBet: number
  minimumRaiseTo: number
  maximumRaiseTo: number
  shortAllInRaiseTo?: number | null
  communityCards: readonly CreditHoldemCard[]
  seats: readonly CreditHoldemSeat[]
  legalActions: readonly CreditHoldemAction[]
  winningSeatIds: readonly string[]
  winningAmount: number
  startedAtUtc: string
  matchDeadlineAtUtc: string
  actionDeadlineAtUtc?: string | null
  remainingActionMilliseconds: number
  tableRule?: CreditHoldemTableRule | null
}>
export type CreditHoldemMatchSession = CreditHoldemSessionBase & Readonly<{
  kind: 'match'
  table: CreditHoldemTable
}>
export type CreditHoldemStanding = Readonly<{
  rank: number
  seatId: string
  displayName: string
  finalStack: number
  status: string
  payoutCredits: number
  isCurrentPlayer: boolean
}>
export type CreditHoldemResultSession = CreditHoldemSessionBase & Readonly<{
  kind: 'result'
  matchId: string
  handNumber: number
  humanCommittedCredits: number
  humanPayoutCredits: number
  houseNetCredits: number
  startedAtUtc: string
  completedAtUtc: string
  standings: readonly CreditHoldemStanding[]
  finalTable: CreditHoldemTable
}>
export type CreditHoldemSession = CreditHoldemIdleSession | CreditHoldemQueueSession
  | CreditHoldemMatchSession | CreditHoldemResultSession
export type CreditHoldemMutationResponse = Readonly<{
  session: CreditHoldemSession
  balanceCredits: number
}>
type CreditHoldemProblem = Readonly<{
  code?: string; error?: string; detail?: string; available?: number; required?: number
}>

export class CreditHoldemRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number

  constructor(message: string, status: number, problem: CreditHoldemProblem | null) {
    super(message)
    this.name = 'CreditHoldemRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
  }
}

export type PendingCreditHoldemMutation = Readonly<{ fingerprint: string; idempotencyKey: string }>
export function createCreditHoldemRequestId(): string {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `credit_holdem_${Date.now()}_${Math.random().toString(36).slice(2, 14)}`
}
export function stableCreditHoldemMutation(
  current: PendingCreditHoldemMutation | null,
  fingerprint: string,
): PendingCreditHoldemMutation {
  return current?.fingerprint === fingerprint
    ? current
    : { fingerprint, idempotencyKey: createCreditHoldemRequestId() }
}

export async function getCreditHoldemStatus(signal?: AbortSignal): Promise<CreditHoldemStatus> {
  return readResponse(await fetchWithAccountSession(`${basePath}/status`, {
    method: 'GET', cache: 'no-store', signal,
  }), isCreditHoldemStatus, 'credit Hold’em status')
}
export async function getCreditHoldemSession(signal?: AbortSignal): Promise<CreditHoldemSession> {
  return readResponse(await fetchWithAccountSession(`${basePath}/session`, {
    method: 'GET', cache: 'no-store', signal,
  }), isCreditHoldemSession, 'credit Hold’em session')
}
export function joinCreditHoldemQueue(
  expectedVersion: number,
  idempotencyKey: string,
  tableRuleId = 'standard',
): Promise<CreditHoldemMutationResponse> {
  return mutationRequest('/queue', {
    method: 'POST', headers: mutationHeaders(idempotencyKey),
    body: JSON.stringify({ expectedVersion, tableRuleId }),
  })
}
export function cancelCreditHoldemQueue(
  ticketId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<CreditHoldemMutationResponse> {
  return mutationRequest(`/queue/${encodeURIComponent(ticketId)}`,
    expectedVersionBody(expectedVersion, idempotencyKey, 'DELETE'))
}
export function postCreditHoldemAction(
  matchId: string,
  action: CreditHoldemAction,
  expectedVersion: number,
  idempotencyKey: string,
  raiseTo?: number,
): Promise<CreditHoldemMutationResponse> {
  return mutationRequest(`/matches/${encodeURIComponent(matchId)}/actions`, {
    method: 'POST', headers: mutationHeaders(idempotencyKey),
    body: JSON.stringify({ type: action, expectedVersion, ...(raiseTo == null ? {} : { raiseTo }) }),
  })
}
export function dealNextCreditHoldemHand(
  matchId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<CreditHoldemMutationResponse> {
  return mutationRequest(`/matches/${encodeURIComponent(matchId)}/next-hand`,
    expectedVersionBody(expectedVersion, idempotencyKey, 'POST'))
}
export function leaveCreditHoldemTable(
  matchId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<CreditHoldemMutationResponse> {
  return mutationRequest(`/matches/${encodeURIComponent(matchId)}/leave`,
    expectedVersionBody(expectedVersion, idempotencyKey, 'POST'))
}
export function isUncertainCreditHoldemFailure(error: unknown): boolean {
  return !(error instanceof CreditHoldemRequestError) || error.status >= 500
}
export function toCreditHoldemPlayingCard(
  card: CreditHoldemCard,
  index: number,
  scope: string,
): PlayingCardModel | null {
  if (card.hidden || card.rank == null || card.suit == null) return null
  return { id: `${scope}-${card.suit}-${card.rank}-${index}`, suit: card.suit, rank: rankValue(card.rank) }
}
export function creditHoldemRaiseTarget(
  table: Pick<CreditHoldemTable, 'minimumRaiseTo' | 'shortAllInRaiseTo'>,
): number {
  return table.shortAllInRaiseTo ?? table.minimumRaiseTo
}

function expectedVersionBody(expectedVersion: number, key: string, method: string): RequestInit {
  return { method, headers: mutationHeaders(key), body: JSON.stringify({ expectedVersion }) }
}
function mutationRequest(path: string, init: RequestInit): Promise<CreditHoldemMutationResponse> {
  return fetchWithAccountSession(`${basePath}${path}`, init)
    .then((response) => readResponse(response, isCreditHoldemMutationResponse, 'credit Hold’em mutation'))
}
async function readResponse<T>(
  response: Response,
  validator: (value: unknown) => value is T,
  label: string,
): Promise<T> {
  const value = await response.json().catch(() => null) as unknown
  if (!response.ok) {
    const problem = isRecord(value) ? value as CreditHoldemProblem : null
    throw new CreditHoldemRequestError(
      problem?.error ?? problem?.detail ?? `${label} request failed (${response.status}).`,
      response.status,
      problem,
    )
  }
  if (hasForbiddenPublicField(value) || !validator(value)) {
    throw new Error(`The server returned an invalid ${label} response.`)
  }
  return value
}
function mutationHeaders(key: string): Headers {
  return new Headers({ 'Content-Type': 'application/json', 'Idempotency-Key': key })
}
function hasForbiddenPublicField(value: unknown): boolean {
  if (typeof value === 'string') return /^(?:bot|actor|user):/i.test(value)
  if (Array.isArray(value)) return value.some(hasForbiddenPublicField)
  if (!isRecord(value)) return false
  return Object.entries(value).some(([key, child]) => {
    const name = key.toLowerCase().replace(/[^a-z0-9]/g, '')
    return name.includes('bot') || name.includes('skill') || name.includes('seed') || name.includes('actor')
      || name === 'deck' || name === 'userid' || name === 'accountid' || name === 'playerid'
      || name.startsWith('raw') || hasForbiddenPublicField(child)
  })
}

function isCreditHoldemStatus(value: unknown): value is CreditHoldemStatus {
  return isRecord(value) && value.available === true && value.minimumStartPlayers === 3
    && value.maximumSeats === 5 && (value.minimumRealPlayers === 1 || value.minimumRealPlayers === 2)
    && isPositiveNumber(value.smallBlindCredits) && isPositiveNumber(value.bigBlindCredits)
    && isPositiveInteger(value.actionDeadlineSeconds) && isPositiveInteger(value.matchDeadlineSeconds)
    && (value.tableRules == null || Array.isArray(value.tableRules)
      && value.tableRules.length > 0 && value.tableRules.every(isCreditHoldemTableRule))
}
function isCreditHoldemMutationResponse(value: unknown): value is CreditHoldemMutationResponse {
  return isRecord(value) && isCreditHoldemSession(value.session) && isNonNegativeNumber(value.balanceCredits)
}
function isCreditHoldemSession(value: unknown): value is CreditHoldemSession {
  if (!isRecord(value) || value.contractVersion !== contractVersion
    || !isNonNegativeInteger(value.version) || typeof value.kind !== 'string') return false
  if (value.kind === 'idle') return true
  if (value.kind === 'queue') return typeof value.ticketId === 'string'
    && isPositiveInteger(value.position) && typeof value.joinedAtUtc === 'string'
    && typeof value.humanGraceEndsAtUtc === 'string' && isSeatList(value.players)
    && (value.tableRule == null || isCreditHoldemTableRule(value.tableRule))
  if (value.kind === 'match') return isCreditHoldemTable(value.table)
  if (value.kind === 'result') return typeof value.matchId === 'string'
    && isPositiveInteger(value.handNumber) && isNonNegativeNumber(value.humanCommittedCredits)
    && isNonNegativeNumber(value.humanPayoutCredits) && typeof value.houseNetCredits === 'number'
    && Number.isFinite(value.houseNetCredits) && typeof value.startedAtUtc === 'string'
    && typeof value.completedAtUtc === 'string' && Array.isArray(value.standings)
    && value.standings.every(isCreditHoldemStanding) && isCreditHoldemTable(value.finalTable)
  return false
}
function isCreditHoldemTable(value: unknown): value is CreditHoldemTable {
  return isRecord(value) && typeof value.matchId === 'string' && typeof value.status === 'string'
    && typeof value.street === 'string' && isPositiveInteger(value.handNumber)
    && Number.isInteger(value.dealerSeat) && Number.isInteger(value.activeSeat)
    && isNonNegativeInteger(value.pot) && isNonNegativeInteger(value.currentBet)
    && isNonNegativeInteger(value.minimumRaiseTo) && isNonNegativeInteger(value.maximumRaiseTo)
    && (value.shortAllInRaiseTo == null || isNonNegativeInteger(value.shortAllInRaiseTo))
    && Array.isArray(value.communityCards) && value.communityCards.every(isCreditHoldemCard)
    && isSeatList(value.seats) && value.seats.length <= 5
    && Array.isArray(value.legalActions) && value.legalActions.every(isCreditHoldemAction)
    && Array.isArray(value.winningSeatIds) && value.winningSeatIds.every((id) => typeof id === 'string')
    && isNonNegativeInteger(value.winningAmount) && typeof value.startedAtUtc === 'string'
    && typeof value.matchDeadlineAtUtc === 'string'
    && (value.actionDeadlineAtUtc == null || typeof value.actionDeadlineAtUtc === 'string')
    && isNonNegativeNumber(value.remainingActionMilliseconds)
    && (value.tableRule == null || isCreditHoldemTableRule(value.tableRule))
}
function isCreditHoldemTableRule(value: unknown): value is CreditHoldemTableRule {
  return isRecord(value) && typeof value.id === 'string' && typeof value.name === 'string'
    && typeof value.description === 'string' && isNonNegativeNumber(value.smallBlindCredits)
    && isPositiveNumber(value.bigBlindCredits) && isNonNegativeNumber(value.anteCredits)
    && isPositiveNumber(value.maximumTableStackCredits)
    && value.maximumTableStackCredits >= value.bigBlindCredits
}
function isSeatList(value: unknown): value is readonly CreditHoldemSeat[] {
  return Array.isArray(value) && value.every(isCreditHoldemSeat)
}
function isCreditHoldemSeat(value: unknown): value is CreditHoldemSeat {
  return isRecord(value) && typeof value.seatId === 'string' && typeof value.displayName === 'string'
    && Number.isInteger(value.seat) && isNonNegativeInteger(value.startingStack)
    && isNonNegativeInteger(value.stack) && isNonNegativeInteger(value.committed)
    && isNonNegativeInteger(value.committedRound) && typeof value.status === 'string'
    && (value.lastAction == null || typeof value.lastAction === 'string')
    && Array.isArray(value.holeCards) && value.holeCards.every(isCreditHoldemCard)
    && (value.handName == null || typeof value.handName === 'string')
    && typeof value.isCurrentPlayer === 'boolean'
}
function isCreditHoldemCard(value: unknown): value is CreditHoldemCard {
  if (!isRecord(value) || typeof value.hidden !== 'boolean') return false
  return value.hidden ? value.rank == null && value.suit == null
    : typeof value.rank === 'string' && isCardSuit(value.suit) && isCardRank(value.rank)
}
function isCreditHoldemStanding(value: unknown): value is CreditHoldemStanding {
  return isRecord(value) && isPositiveInteger(value.rank) && typeof value.seatId === 'string'
    && typeof value.displayName === 'string' && isNonNegativeInteger(value.finalStack)
    && typeof value.status === 'string' && isNonNegativeNumber(value.payoutCredits)
    && typeof value.isCurrentPlayer === 'boolean'
}
function isCreditHoldemAction(value: unknown): value is CreditHoldemAction {
  return value === 'fold' || value === 'check' || value === 'call' || value === 'raise'
}
function isCardSuit(value: unknown): value is CardSuit {
  return value === 'clubs' || value === 'diamonds' || value === 'hearts' || value === 'spades'
}
function isCardRank(value: string): boolean {
  return value === 'A' || value === 'K' || value === 'Q' || value === 'J'
    || (Number.isInteger(Number(value)) && Number(value) >= 2 && Number(value) <= 10)
}
function rankValue(rank: string): CardRank {
  if (rank === 'A') return 1
  if (rank === 'J') return 11
  if (rank === 'Q') return 12
  if (rank === 'K') return 13
  return Number(rank) as CardRank
}
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}
function isPositiveInteger(value: unknown): value is number {
  return Number.isInteger(value) && typeof value === 'number' && value > 0
}
function isNonNegativeInteger(value: unknown): value is number {
  return Number.isInteger(value) && typeof value === 'number' && value >= 0
}
function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}
function isNonNegativeNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

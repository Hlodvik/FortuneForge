import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'
import type { CardRank, CardSuit } from '../shared/cards'
import type {
  SolitaireBuyIn,
  SolitaireCard,
  SolitaireCommand,
  SolitaireCommandRequest,
  SolitaireDrawCount,
  SolitaireGame,
  SolitaireHistoryItem,
  SolitaireMatchSession,
  SolitaireMutationResponse,
  SolitairePlayer,
  SolitairePlayerCount,
  SolitaireSession,
  SolitaireStanding,
} from './solitaireTypes'

type SolitaireProblem = Readonly<{
  code?: string
  error?: string
  detail?: string
  available?: number
  required?: number
}>

export class SolitaireRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number

  constructor(message: string, status: number, problem: SolitaireProblem | null) {
    super(message)
    this.name = 'SolitaireRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
  }
}

export type PendingSolitaireMutation = Readonly<{
  fingerprint: string
  idempotencyKey: string
}>

export type ReconciledSolitaireMutation = Readonly<{
  session: SolitaireSession
  mutation: SolitaireMutationResponse | null
  reconciled: boolean
}>

export type SolitaireCommandTransport = Readonly<{
  command: (
    matchId: string,
    command: SolitaireCommandRequest,
    idempotencyKey: string,
  ) => Promise<SolitaireMutationResponse>
  session: () => Promise<SolitaireSession>
}>

export function createSolitaireRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `solitaire_${Date.now()}_${Math.random().toString(36).slice(2, 14)}`
}

export function stableSolitaireMutation(
  current: PendingSolitaireMutation | null,
  fingerprint: string,
): PendingSolitaireMutation {
  return current?.fingerprint === fingerprint
    ? current
    : { fingerprint, idempotencyKey: createSolitaireRequestId() }
}

export async function getSolitaireSession(signal?: AbortSignal): Promise<SolitaireSession> {
  const response = await fetchWithAccountSession('/api/solitaire/session', {
    method: 'GET',
    cache: 'no-store',
    signal,
  })
  return readResponse(response, isSolitaireSession, 'Solitaire session')
}

export async function getSolitaireHistory(
  limit = 30,
  signal?: AbortSignal,
): Promise<readonly SolitaireHistoryItem[]> {
  const response = await fetchWithAccountSession(
    `/api/solitaire/history?limit=${encodeURIComponent(limit)}`,
    { method: 'GET', cache: 'no-store', signal },
  )
  return readResponse(
    response,
    (value): value is readonly SolitaireHistoryItem[] =>
      Array.isArray(value) && value.every(isSolitaireHistoryItem),
    'Solitaire history',
  )
}

export async function joinSolitaireQueue(
  playerCount: SolitairePlayerCount,
  buyInCredits: SolitaireBuyIn,
  drawCount: SolitaireDrawCount,
  idempotencyKey: string,
): Promise<SolitaireMutationResponse> {
  return mutationRequest('/queue', {
    method: 'POST',
    headers: mutationHeaders(idempotencyKey),
    body: JSON.stringify({ playerCount, buyInCredits, drawCount, idempotencyKey }),
  })
}

export async function cancelSolitaireQueue(
  ticketId: string,
  idempotencyKey: string,
): Promise<SolitaireMutationResponse> {
  return mutationRequest(`/queue/${encodeURIComponent(ticketId)}`, {
    method: 'DELETE',
    headers: mutationHeaders(idempotencyKey),
  })
}

export async function postSolitaireCommand(
  matchId: string,
  command: SolitaireCommandRequest,
  idempotencyKey: string,
): Promise<SolitaireMutationResponse> {
  return mutationRequest(`/matches/${encodeURIComponent(matchId)}/commands`, {
    method: 'POST',
    headers: mutationHeaders(idempotencyKey),
    body: JSON.stringify(command),
  })
}

export async function postSolitaireForfeit(
  matchId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<SolitaireMutationResponse> {
  return mutationRequest(`/matches/${encodeURIComponent(matchId)}/forfeit`, {
    method: 'POST',
    headers: mutationHeaders(idempotencyKey),
    body: JSON.stringify({ expectedVersion }),
  })
}

export async function dismissSolitaireResult(
  matchId: string,
  idempotencyKey: string,
): Promise<SolitaireMutationResponse> {
  return mutationRequest(`/matches/${encodeURIComponent(matchId)}/dismiss`, {
    method: 'POST',
    headers: mutationHeaders(idempotencyKey),
  })
}

export async function claimSolitaireResult(
  matchId: string,
  idempotencyKey: string,
): Promise<SolitaireMutationResponse> {
  return mutationRequest(`/matches/${encodeURIComponent(matchId)}/claim`, {
    method: 'POST',
    headers: mutationHeaders(idempotencyKey),
  })
}

export async function postCommandWithReconciliation(
  match: Pick<SolitaireMatchSession, 'matchId' | 'version'>,
  command: SolitaireCommand,
  idempotencyKey: string,
  transport: SolitaireCommandTransport = {
    command: postSolitaireCommand,
    session: getSolitaireSession,
  },
): Promise<ReconciledSolitaireMutation> {
  try {
    const mutation = await transport.command(
      match.matchId,
      { ...command, expectedVersion: match.version },
      idempotencyKey,
    )
    return { session: mutation.session, mutation, reconciled: false }
  } catch (error) {
    if (!(error instanceof SolitaireRequestError)
      || error.code !== 'solitaire-state-conflict') {
      throw error
    }
    return {
      session: await transport.session(),
      mutation: null,
      reconciled: true,
    }
  }
}

export function isUncertainSolitaireFailure(error: unknown): boolean {
  return !(error instanceof SolitaireRequestError) || error.status >= 500
}

function mutationRequest(path: string, init: RequestInit): Promise<SolitaireMutationResponse> {
  return fetchWithAccountSession(`/api/solitaire${path}`, init)
    .then((response) => readResponse(
      response,
      isSolitaireMutationResponse,
      'Solitaire mutation',
    ))
}

async function readResponse<T>(
  response: Response,
  validator: (value: unknown) => value is T,
  label: string,
): Promise<T> {
  const value = await response.json().catch(() => null) as unknown
  if (!response.ok) {
    const problem = isRecord(value) ? value as SolitaireProblem : null
    throw new SolitaireRequestError(
      problem?.error ?? problem?.detail ?? `${label} request failed (${response.status}).`,
      response.status,
      problem,
    )
  }
  if (!validator(value)) {
    throw new Error(`The server returned an invalid ${label.toLowerCase()} response.`)
  }
  return value
}

function mutationHeaders(idempotencyKey: string): Headers {
  return new Headers({
    'Content-Type': 'application/json',
    'Idempotency-Key': idempotencyKey,
  })
}

function isSolitaireMutationResponse(value: unknown): value is SolitaireMutationResponse {
  return isRecord(value)
    && isSolitaireSession(value.session)
    && isNonNegativeNumber(value.balanceCredits)
}

function isSolitaireSession(value: unknown): value is SolitaireSession {
  if (!isRecord(value) || typeof value.kind !== 'string') return false
  if (value.kind === 'idle') return true
  if (value.kind === 'queued') {
    return typeof value.ticketId === 'string'
      && isPlayerCount(value.playerCount)
      && isBuyIn(value.buyInCredits)
      && isNonNegativeNumber(value.prizePoolCredits)
      && isNonNegativeNumber(value.winnerPayoutCredits)
      && Number.isInteger(value.position)
      && typeof value.joinedAtUtc === 'string'
      && isPlayerList(value.players)
  }
  if (value.kind === 'match') {
    return typeof value.matchId === 'string'
      && isPlayerCount(value.playerCount)
      && isBuyIn(value.buyInCredits)
      && isNonNegativeNumber(value.prizePoolCredits)
      && isNonNegativeNumber(value.winnerPayoutCredits)
      && !hasOwn(value, 'dealSeed')
      && typeof value.startedAtUtc === 'string'
      && typeof value.deadlineAtUtc === 'string'
      && typeof value.version === 'number'
      && Number.isInteger(value.version)
      && value.version > 0
      && isNonNegativeNumber(value.score)
      && isNonNegativeNumber(value.moves)
      && isNonNegativeNumber(value.remainingMilliseconds)
      && typeof value.isPaused === 'boolean'
      && isNonNegativeNumber(value.pauseRemainingMilliseconds)
      && typeof value.canUndo === 'boolean'
      && (value.integrityWarning == null || isSolitaireIntegrityWarning(value.integrityWarning))
      && isSolitaireGame(value.game)
      && isPlayerList(value.players)
      && value.players.length === value.playerCount
  }
  if (value.kind === 'result') {
    return typeof value.matchId === 'string'
      && isPlayerCount(value.playerCount)
      && isBuyIn(value.buyInCredits)
      && isNonNegativeNumber(value.prizePoolCredits)
      && isNonNegativeNumber(value.winnerPayoutCredits)
      && isNonNegativeNumber(value.platformFeeCredits)
      && typeof value.startedAtUtc === 'string'
      && typeof value.completedAtUtc === 'string'
      && (value.claimStatus === 'unclaimed' || value.claimStatus === 'completed')
      && typeof value.canClaim === 'boolean'
      && Array.isArray(value.standings)
      && value.standings.every(isSolitaireStanding)
  }
  return false
}

function isSolitaireIntegrityWarning(value: unknown): boolean {
  return isRecord(value)
    && typeof value.warningId === 'string'
    && value.warningId.startsWith('warning-')
    && typeof value.reason === 'string'
    && typeof value.purpose === 'string'
    && typeof value.occurredAtUtc === 'string'
    && typeof value.acknowledged === 'boolean'
}

function isSolitaireGame(value: unknown): value is SolitaireGame {
  return isRecord(value)
    && isCardList(value.stock)
    && isCardList(value.waste)
    && isPileList(value.foundations, 4)
    && isPileList(value.tableau, 7)
    && Number.isInteger(value.drawCount)
    && isNonNegativeNumber(value.score)
    && isNonNegativeNumber(value.moves)
    && !hasOwn(value, 'seed')
    && typeof value.message === 'string'
}

function isSolitaireCard(value: unknown): value is SolitaireCard {
  if (!isRecord(value)
    || typeof value.isFaceUp !== 'boolean'
    || hasOwn(value, 'faceUp')) return false
  if (!value.isFaceUp) {
    return value.id == null && value.suit == null && value.rank == null
  }
  return typeof value.id === 'string'
    && isCardSuit(value.suit)
    && isCardRank(value.rank)
}

function isSolitairePlayer(value: unknown): value is SolitairePlayer {
  if (!isRecord(value) || typeof value.playerId !== 'string') return false
  const valid = typeof value.displayName === 'string'
    && Number.isInteger(value.seat)
    && typeof value.joinedAtUtc === 'string'
    && isPlayerStatus(value.status)
    && typeof value.isCurrentPlayer === 'boolean'
    && isOptionalNonNegativeInteger(value.score)
    && isOptionalNonNegativeInteger(value.moves)
    && isOptionalNonNegativeInteger(value.elapsedSeconds)
  if (!valid) return false
  return value.status !== 'open'
    || (value.playerId.startsWith('open-seat-')
      && value.displayName === 'Open seat'
      && value.isCurrentPlayer === false)
}

function isOptionalNonNegativeInteger(value: unknown): boolean {
  return value == null || (Number.isInteger(value) && Number(value) >= 0)
}

function isSolitaireStanding(value: unknown): value is SolitaireStanding {
  return isRecord(value)
    && Number.isInteger(value.rank)
    && typeof value.playerId === 'string'
    && typeof value.displayName === 'string'
    && isNonNegativeNumber(value.score)
    && isNonNegativeNumber(value.moves)
    && isNonNegativeNumber(value.elapsedSeconds)
    && (value.status === 'finished' || value.status === 'forfeited'
      || value.status === 'integrity-failed')
    && isNonNegativeNumber(value.payoutCredits)
    && typeof value.isCurrentPlayer === 'boolean'
}

function isSolitaireHistoryItem(value: unknown): value is SolitaireHistoryItem {
  return isRecord(value)
    && typeof value.matchId === 'string'
    && isPlayerCount(value.playerCount)
    && isBuyIn(value.buyInCredits)
    && isNonNegativeNumber(value.prizePoolCredits)
    && Number.isInteger(value.placement)
    && isNonNegativeNumber(value.score)
    && isNonNegativeNumber(value.elapsedSeconds)
    && isNonNegativeNumber(value.payoutCredits)
    && typeof value.netCredits === 'number'
    && Number.isFinite(value.netCredits)
    && typeof value.completedAtUtc === 'string'
    && Array.isArray(value.opponents)
    && value.opponents.every((opponent) => typeof opponent === 'string')
}

function isPlayerList(value: unknown): value is readonly SolitairePlayer[] {
  return Array.isArray(value) && value.every(isSolitairePlayer)
}

function isCardList(value: unknown): value is readonly SolitaireCard[] {
  return Array.isArray(value) && value.every(isSolitaireCard)
}

function isPileList(value: unknown, count: number): value is readonly (readonly SolitaireCard[])[] {
  return Array.isArray(value) && value.length === count && value.every(isCardList)
}

function isPlayerCount(value: unknown): value is SolitairePlayerCount {
  return value === 4 || value === 6 || value === 8
}

function isPlayerStatus(value: unknown): value is SolitairePlayer['status'] {
  return value === 'open' || value === 'queued' || value === 'playing'
    || value === 'finished' || value === 'forfeited' || value === 'integrity-failed'
}

function isBuyIn(value: unknown): value is SolitaireBuyIn {
  return value === 5 || value === 10 || value === 25
}

function isCardSuit(value: unknown): value is CardSuit {
  return value === 'clubs' || value === 'diamonds'
    || value === 'hearts' || value === 'spades'
}

function isCardRank(value: unknown): value is CardRank {
  return Number.isInteger(value) && Number(value) >= 1 && Number(value) <= 13
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function hasOwn(value: Record<string, unknown>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(value, key)
}

function isNonNegativeNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

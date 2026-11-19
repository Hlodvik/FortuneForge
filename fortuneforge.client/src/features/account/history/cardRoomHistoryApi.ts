// Edited: 2026-11-18T22:53:33-06:00 | Computer: Vinnabok
import type { CardRoomGame } from './cardRoomHistoryTypes'
import { projectCardRoomSession } from './cardRoomHistoryModel'
import { requestCardRoomSession, requestSolitaireResultClaim, type ActiveCardRoomGame } from '../services/cardRoomActivityApi'
import { fetchWithAccountSession } from '../services/accountsApi'

export type CardRoomHistoryResult = Readonly<{
  resultId: string
  game: CardRoomGame
  mode: string
  matchId: string
  startedAtUtc: string
  completedAtUtc: string | null
  unseen: boolean
  requiresClaim: boolean
  winningsCredits: number
  wagerCredits: number
  netCredits: number
  score: number | null
  moves: number | null
  schemaVersion: number
}>

/** Account history reads a summary projection without importing playable clients. */
export async function getActiveCardRoomActivity(game: ActiveCardRoomGame, signal?: AbortSignal) {
  const response = await requestCardRoomSession(game, signal)
  if (!response.ok) throw await historyError(response, 'The active game could not be loaded.')
  return projectCardRoomSession(game, await response.json())
}

export async function claimSolitaireHistoryResult(matchId: string, idempotencyKey: string) {
  const response = await requestSolitaireResultClaim(matchId, idempotencyKey)
  if (!response.ok) throw await historyError(response, 'The game result could not be claimed.')
  const body: unknown = await response.json()
  if (!isRecord(body) || typeof body.balanceCredits !== 'number'
    || !Number.isFinite(body.balanceCredits) || body.balanceCredits < 0) {
    throw new Error('The game claim returned an invalid balance.')
  }
  return { balanceCredits: body.balanceCredits }
}

export async function getCardRoomHistory(
  limit = 40,
  signal?: AbortSignal,
): Promise<readonly CardRoomHistoryResult[]> {
  const response = await fetchWithAccountSession(
    `/api/cards/history?limit=${encodeURIComponent(limit)}`,
    { method: 'GET', cache: 'no-store', signal },
  )
  if (!response.ok) throw await historyError(response, 'Game history could not be loaded.')
  const body: unknown = await response.json()
  if (!Array.isArray(body) || !body.every(isHistoryResult)) {
    throw new Error('Game history returned an invalid response.')
  }
  return body
}

export async function markCardRoomResultSeen(resultId: string): Promise<void> {
  const response = await fetchWithAccountSession(
    `/api/cards/history/${encodeURIComponent(resultId)}/seen`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID().replaceAll('-', '') },
    },
  )
  if (!response.ok) throw await historyError(response, 'The result could not be opened.')
}

function isHistoryResult(value: unknown): value is CardRoomHistoryResult {
  if (!isRecord(value)) return false
  return typeof value.resultId === 'string'
    && (value.game === 'blackjack' || value.game === 'texas-holdem' || value.game === 'three-card-poker' || value.game === 'house-holdem' || value.game === 'solitaire' || value.game === 'hi-lo' || value.game === 'andar-bahar' || value.game === 'mines')
    && typeof value.mode === 'string'
    && typeof value.matchId === 'string'
    && isIsoDate(value.startedAtUtc)
    && (value.completedAtUtc === null || isIsoDate(value.completedAtUtc))
    && typeof value.unseen === 'boolean'
    && typeof value.requiresClaim === 'boolean'
    && Number.isFinite(value.winningsCredits)
    && Number.isFinite(value.wagerCredits)
    && Number.isFinite(value.netCredits)
    && (value.score === null || Number.isInteger(value.score))
    && (value.moves === null || Number.isInteger(value.moves))
    && Number.isInteger(value.schemaVersion)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isIsoDate(value: unknown): value is string {
  return typeof value === 'string' && Number.isFinite(Date.parse(value))
}

async function historyError(response: Response, fallback: string): Promise<Error> {
  const body: unknown = await response.json().catch(() => null)
  return new Error(isRecord(body) && typeof body.error === 'string' ? body.error : fallback)
}

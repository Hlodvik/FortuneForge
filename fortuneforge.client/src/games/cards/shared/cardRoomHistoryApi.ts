import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'

export type CardRoomHistoryResult = Readonly<{
  resultId: string
  game: 'blackjack' | 'texas-holdem' | 'solitaire'
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
    && (value.game === 'blackjack' || value.game === 'texas-holdem' || value.game === 'solitaire')
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

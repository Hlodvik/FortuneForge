export const PRACTICE_BOT_CONTRACT = 'cards.bot.v2'
export const PRACTICE_BOT_SKILLS = [2, 3, 4] as const

export type PracticeBotSkill = (typeof PRACTICE_BOT_SKILLS)[number]

export type PracticeSeat = Readonly<{
  seatId: string
  displayName: string
  seat: number
  status: string
}>

export type PracticeQueue = Readonly<{
  queueId: string
  game: string
  requiredPlayers: number
  seats: readonly PracticeSeat[]
}>

export type PracticeCommand = Readonly<{
  type: string
  expectedVersion: number
  arguments?: Readonly<Record<string, string>>
}>

type PracticeProblem = Readonly<{
  code?: string
  error?: string
  detail?: string
}>

export class PracticeBotRequestError extends Error {
  readonly status: number
  readonly code?: string

  constructor(message: string, status: number, problem: PracticeProblem | null) {
    super(message)
    this.name = 'PracticeBotRequestError'
    this.status = status
    this.code = problem?.code
  }
}

export type PendingPracticeMutation = Readonly<{
  fingerprint: string
  idempotencyKey: string
}>

export function stablePracticeMutation(
  pending: PendingPracticeMutation | null,
  fingerprint: string,
): PendingPracticeMutation {
  if (pending?.fingerprint === fingerprint) return pending
  return { fingerprint, idempotencyKey: createPracticeRequestId() }
}

export async function getPracticeSession<T>(
  basePath: string,
  sessionId: string,
  signal?: AbortSignal,
): Promise<T | null> {
  const response = await fetch(`${basePath}/session`, {
    method: 'GET',
    credentials: 'omit',
    cache: 'no-store',
    headers: practiceHeaders(sessionId),
    signal,
  })
  if (response.status === 404) return null
  return readPracticeResponse<T>(response)
}

export async function joinPracticeQueue<T>(
  basePath: string,
  sessionId: string,
  playerCount: number,
  difficulty: PracticeBotSkill,
  idempotencyKey: string,
): Promise<T> {
  const response = await fetch(`${basePath}/queue`, {
    method: 'POST',
    credentials: 'omit',
    cache: 'no-store',
    headers: practiceHeaders(sessionId, idempotencyKey),
    body: JSON.stringify({ playerCount, difficulty, idempotencyKey }),
  })
  return readPracticeResponse<T>(response)
}

export async function postPracticeCommand<T>(
  basePath: string,
  sessionId: string,
  matchId: string,
  command: PracticeCommand,
  idempotencyKey: string,
): Promise<T> {
  const response = await fetch(`${basePath}/matches/${encodeURIComponent(matchId)}/commands`, {
    method: 'POST',
    credentials: 'omit',
    cache: 'no-store',
    headers: practiceHeaders(sessionId, idempotencyKey),
    body: JSON.stringify({ ...command, idempotencyKey }),
  })
  return readPracticeResponse<T>(response)
}

export function practiceSessionId(storageKey: string): string {
  const fallbackKey = `fortune_forge_${storageKey}_${createPracticeRequestId()}`
  if (typeof window === 'undefined') return fallbackKey
  try {
    const existing = window.localStorage.getItem(storageKey)
    if (existing !== null && existing.length >= 16) return existing
    window.localStorage.setItem(storageKey, fallbackKey)
  } catch {
    return fallbackKey
  }
  return fallbackKey
}

export function createPracticeRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `practice_${Date.now()}_${Math.random().toString(36).slice(2, 14)}`
}

export function isUncertainPracticeFailure(error: unknown): boolean {
  return !(error instanceof PracticeBotRequestError) || error.status >= 500
}

async function readPracticeResponse<T>(response: Response): Promise<T> {
  const value = await response.json().catch(() => null) as unknown
  if (!response.ok) {
    const problem = isRecord(value) ? value as PracticeProblem : null
    throw new PracticeBotRequestError(
      problem?.error ?? problem?.detail ?? `Practice table request failed (${response.status}).`,
      response.status,
      problem,
    )
  }
  if (!isRecord(value) || value.contractVersion !== PRACTICE_BOT_CONTRACT) {
    throw new Error('The practice table returned an unsupported contract.')
  }
  return value as T
}

function practiceHeaders(sessionId: string, idempotencyKey?: string): Headers {
  const headers = new Headers({
    'Content-Type': 'application/json',
    'X-Practice-Session-Id': sessionId,
  })
  if (idempotencyKey !== undefined) headers.set('Idempotency-Key', idempotencyKey)
  return headers
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

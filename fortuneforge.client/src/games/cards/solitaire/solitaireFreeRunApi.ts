import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'
import { createSolitaireRequestId, SolitaireRequestError } from './solitaireApi'
import type { SolitaireCommand, SolitaireDrawCount } from './solitaireTypes'

export type SolitaireFreeReplayCommand = Extract<
  SolitaireCommand,
  { type: 'draw' | 'flip' | 'move' }
>

export type SolitaireFreeRun = Readonly<{
  runId: string
  seed: number
  drawCount: SolitaireDrawCount
  startedAtUtc: string
  wasAlreadyStarted: boolean
}>

export type SolitaireFreeRunCompletion = Readonly<{
  runId: string
  seed: number
  drawCount: SolitaireDrawCount
  score: number
  moves: number
  elapsedMilliseconds: number
  terminal: 'won' | 'submitted'
  completedAtUtc: string
  wasAlreadyCompleted: boolean
}>

export function createSolitaireFreeRunRequestId(): string {
  return `free_${createSolitaireRequestId().replaceAll('-', '_')}`
}

export async function startSolitaireFreeRun(
  drawCount: SolitaireDrawCount,
  idempotencyKey: string,
): Promise<SolitaireFreeRun> {
  const response = await fetchWithAccountSession('/api/solitaire/free/runs', {
    method: 'POST',
    headers: new Headers({
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    }),
    body: JSON.stringify({ drawCount }),
  })
  return readFreeResponse(response, isStartResponse, 'Solitaire free-run start')
}

export async function completeSolitaireFreeRun(
  runId: string,
  commands: readonly SolitaireFreeReplayCommand[],
): Promise<SolitaireFreeRunCompletion> {
  const response = await fetchWithAccountSession(
    `/api/solitaire/free/runs/${encodeURIComponent(runId)}/replay`,
    {
      method: 'POST',
      headers: new Headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ commands }),
    },
  )
  return readFreeResponse(response, isCompletionResponse, 'Solitaire free-run completion')
}

async function readFreeResponse<T>(
  response: Response,
  validator: (value: unknown) => value is T,
  label: string,
): Promise<T> {
  const value = await response.json().catch(() => null) as unknown
  if (!response.ok) {
    const problem = isRecord(value) ? value : null
    throw new SolitaireRequestError(
      typeof problem?.error === 'string'
        ? problem.error
        : `${label} request failed (${response.status}).`,
      response.status,
      problem,
    )
  }
  if (!validator(value)) throw new Error(`The server returned an invalid ${label.toLowerCase()} response.`)
  return value
}

function isStartResponse(value: unknown): value is SolitaireFreeRun {
  return isRecord(value)
    && typeof value.runId === 'string'
    && value.runId.startsWith('solitaire_free_')
    && isUint32(value.seed)
    && isDrawCount(value.drawCount)
    && typeof value.startedAtUtc === 'string'
    && typeof value.wasAlreadyStarted === 'boolean'
}

function isCompletionResponse(value: unknown): value is SolitaireFreeRunCompletion {
  return isRecord(value)
    && typeof value.runId === 'string'
    && isUint32(value.seed)
    && isDrawCount(value.drawCount)
    && isNonNegativeInteger(value.score)
    && isNonNegativeInteger(value.moves)
    && isNonNegativeInteger(value.elapsedMilliseconds)
    && (value.terminal === 'won' || value.terminal === 'submitted')
    && typeof value.completedAtUtc === 'string'
    && typeof value.wasAlreadyCompleted === 'boolean'
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isDrawCount(value: unknown): value is SolitaireDrawCount {
  return value === 1 || value === 3
}

function isUint32(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) > 0 && Number(value) <= 0xffffffff
}

function isNonNegativeInteger(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) >= 0
}

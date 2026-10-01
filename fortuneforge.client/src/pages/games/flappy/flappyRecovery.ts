import { FlappyReplaySession, maximumFlappyReplayFlaps, maximumFlappyReplayTicks,
  type FlappyReplayPayload, type FlappyReplayCompletion as LocalCompletion } from '@fortuneforge/games-flappy'
import type { FlappyFreeRun } from '../../../games/arcade/arcadeCompetitionApi'

export type FlightCursor = Readonly<{ totalTicks: number; flapTicks: readonly number[] }>
export type FlightSubmission = Readonly<{ run: FlappyFreeRun | null; runId: string; replay: FlappyReplayPayload; display: LocalCompletion['display'] }>
export type FlappyRecovery = Readonly<{ kind: 'start'; idempotencyKey: string }>
  | Readonly<{ kind: 'flight'; run: FlappyFreeRun; cursor: FlightCursor }>
  | Readonly<{ kind: 'submission'; submission: FlightSubmission }>

export const recoveryKey = (userId: string) => `fortuneforge:flappy:recovery:${userId}`
const oldStartKey = (userId: string) => `fortuneforge:flappy:start:${userId}`
const oldSubmissionKey = (userId: string) => `fortuneforge:flappy:pending:${userId}`
const storageMessage = 'This browser could not save your flight. Retry when browser storage is available.'

export function readFlappyRecovery(userId: string): { recovery: FlappyRecovery | null; error: string | null } {
  if (typeof window === 'undefined') return { recovery: null, error: null }
  try {
    const saved = sessionStorage.getItem(recoveryKey(userId))
    if (saved !== null) return { recovery: parseRecovery(JSON.parse(saved)), error: null }
    const submission = sessionStorage.getItem(oldSubmissionKey(userId))
    if (submission !== null) {
      const legacy: unknown = JSON.parse(submission)
      if (!record(legacy)) throw new Error('Invalid saved flight.')
      return { recovery: parseRecovery({ kind: 'submission', submission: { ...legacy, run: null } }), error: null }
    }
    const start = sessionStorage.getItem(oldStartKey(userId))
    return { recovery: start === null ? null : parseRecovery({ kind: 'start', ...JSON.parse(start) }), error: null }
  } catch { return { recovery: null, error: 'Your saved flight could not be opened. Clear it before starting a new flight.' } }
}

export function writeFlappyRecovery(userId: string, recovery: FlappyRecovery): string | null {
  try {
    sessionStorage.setItem(recoveryKey(userId), JSON.stringify(recovery))
    return null
  } catch { return storageMessage }
}

export function clearFlappyRecovery(userId: string): string | null {
  try {
    sessionStorage.removeItem(recoveryKey(userId))
    sessionStorage.removeItem(oldStartKey(userId))
    sessionStorage.removeItem(oldSubmissionKey(userId))
    return null
  } catch { return storageMessage }
}

export function restoreFlight(run: FlappyFreeRun, cursor: FlightCursor): FlappyReplaySession {
  const session = new FlappyReplaySession(run.runId, run.seed)
  let index = 0
  for (let tick = 0; tick < cursor.totalTicks; tick++) {
    if (session.view.status !== 'running') throw new Error('Input after the flight ended.')
    const flap = cursor.flapTicks[index] === tick
    if (flap) index++
    session.advanceFrame(flap)
  }
  return session
}

function parseRecovery(value: unknown): FlappyRecovery {
  if (!record(value)) throw new Error('Invalid saved flight.')
  if (value.kind === 'start' && typeof value.idempotencyKey === 'string' && /^[A-Za-z0-9_-]{16,128}$/.test(value.idempotencyKey)) {
    return { kind: 'start', idempotencyKey: value.idempotencyKey }
  }
  if (value.kind === 'flight' && isRun(value.run) && isCursor(value.cursor, true)) {
    const session = restoreFlight(value.run, value.cursor)
    const completion = session.takeCompletion()
    if (completion) return { kind: 'submission', submission: { run: value.run, runId: value.run.runId, ...completion } }
    if (session.view.status !== 'running') throw new Error('Unrecordable saved flight.')
    return { kind: 'flight', run: value.run, cursor: value.cursor }
  }
  const s = value.submission
  if (value.kind === 'submission' && record(s) && isRunId(s.runId) && isCursor(s.replay, false) && isDisplay(s.display)
    && (s.run === null || isRun(s.run) && s.run.runId === s.runId)) {
    if (s.run !== null) {
      const session = restoreFlight(s.run, s.replay)
      const completion = session.takeCompletion()
      if (!completion || completion.display.score !== s.display.score || completion.display.phase !== s.display.phase) throw new Error('Invalid saved result.')
    }
    return { kind: 'submission', submission: { run: s.run, runId: s.runId, replay: s.replay, display: s.display } }
  }
  throw new Error('Invalid saved flight.')
}
function isRunId(value: unknown): value is string { return typeof value === 'string' && /^flappy_free_[0-9a-f]{64}$/.test(value) }
function isRun(value: unknown): value is FlappyFreeRun {
  return record(value) && isRunId(value.runId) && Number.isInteger(value.seed) && (value.seed as number) > 0 && (value.seed as number) <= 0xffff_ffff
    && typeof value.startedAtUtc === 'string' && Number.isFinite(Date.parse(value.startedAtUtc)) && typeof value.wasReplay === 'boolean'
}
function isCursor(value: unknown, partial: boolean): value is FlightCursor {
  if (!record(value) || !Number.isInteger(value.totalTicks) || (value.totalTicks as number) < (partial ? 0 : 1)
    || (value.totalTicks as number) > maximumFlappyReplayTicks || !Array.isArray(value.flapTicks) || value.flapTicks.length > maximumFlappyReplayFlaps) return false
  let prior = -1
  return value.flapTicks.every(tick => {
    if (!Number.isInteger(tick) || tick < 0 || tick >= (value.totalTicks as number) || tick <= prior) return false
    prior = tick
    return true
  })
}
function isDisplay(value: unknown): value is LocalCompletion['display'] {
  return record(value) && Number.isSafeInteger(value.score) && (value.score as number) >= 0
    && ['ground-collision', 'ceiling-collision', 'obstacle-collision'].includes(value.phase as string)
}
function record(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }

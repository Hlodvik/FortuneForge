import { ArcadeCompetitionRequestError } from '../../../games/arcade/arcadeCompetitionApi'
import type { AsteroidsReplayDisplayResult, AsteroidsReplayPayload } from '@fortuneforge/games-asteroids'
import type { ArcadeCompetitionPaidPeriod } from '../../../games/arcade/arcadeCompetitionApi'

export type PendingPaidAsteroidsSubmission = Readonly<{
  kind: 'paid'
  period: ArcadeCompetitionPaidPeriod
  runId: string
  replay: AsteroidsReplayPayload
  display: AsteroidsReplayDisplayResult
}>

export type PendingFreeAsteroidsSubmission = Readonly<{
  kind: 'free'
  runId: string
  replay: AsteroidsReplayPayload
  display: AsteroidsReplayDisplayResult
}>

export type PendingAsteroidsSubmission = PendingPaidAsteroidsSubmission | PendingFreeAsteroidsSubmission

export function createAsteroidsIdempotencyKey(): string {
  return `asteroids-${randomHex(16)}`
}

/** Stores the exact completed replay so retries cannot start or rebuild a paid run. */
export function createPendingPaidAsteroidsSubmission(
  period: ArcadeCompetitionPaidPeriod,
  runId: string,
  replay: AsteroidsReplayPayload,
  display: AsteroidsReplayDisplayResult,
): PendingPaidAsteroidsSubmission {
  return { kind: 'paid', period, runId, replay, display }
}

/** Stores the exact completed replay so a failed free-run completion can be retried idempotently. */
export function createPendingFreeAsteroidsSubmission(
  runId: string,
  replay: AsteroidsReplayPayload,
  display: AsteroidsReplayDisplayResult,
): PendingFreeAsteroidsSubmission {
  return { kind: 'free', runId, replay, display }
}

export function paidOfficialScoreText(score: number): string {
  return `Official score: ${score}`
}


export function friendlyAsteroidsCompetitionError(reason: unknown): string {
  if (reason instanceof ArcadeCompetitionRequestError) {
    if (reason.status === 401 || reason.status === 403) return 'Your session needs to be refreshed before you can enter a competition.'
    if (reason.status === 402 || reason.code?.includes('insufficient') === true) return 'You need at least R1 in your balance to enter this competition.'
    if (reason.code?.includes('closed') === true || reason.code?.includes('cutoff') === true || reason.status === 410) return 'That competition has just closed. Please choose another available competition.'
    if (reason.status === 409 || reason.code?.includes('replay') === true || reason.code?.includes('run') === true) return 'This run can no longer be submitted. Please return to the lobby and start a new one.'
  }
  if (reason instanceof TypeError) return 'We could not reach the competition service. Check your connection and try again.'
  return 'We could not complete that competition action. Please try again.'
}

function randomHex(byteLength: number): string {
  const crypto = globalThis.crypto
  if (crypto === undefined || typeof crypto.getRandomValues !== 'function') {
    throw new Error('Secure random values are unavailable.')
  }
  const bytes = crypto.getRandomValues(new Uint8Array(byteLength))
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
}

import { useCallback, useEffect, useRef, useState } from 'react'
import { AsteroidsReplayPlay, maximumReplayCommands, maximumReplaySteps } from '@fortuneforge/games-asteroids'
import type { AsteroidsReplayDisplayResult, AsteroidsReplayPayload } from '@fortuneforge/games-asteroids'
import '@fortuneforge/games-asteroids/styles.css'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { ArcadeCompetitionLeaderboard } from '../../../games/arcade/ArcadeCompetitionLeaderboard'
import {
  ArcadeCompetitionRequestError,
} from '../../../games/arcade/arcadeCompetitionApi'
import type {
  ArcadeCompetitionGateway,
  ArcadeCompetitionPaidPeriod,
  AsteroidsFreeRunGateway,
  AsteroidsPaidCompetitionGateway,
} from '../../../games/arcade/arcadeCompetitionApi'
import {
  createAsteroidsIdempotencyKey,
  createPendingFreeAsteroidsSubmission,
  createPendingPaidAsteroidsSubmission,
  friendlyAsteroidsCompetitionError,
  paidOfficialScoreText,
} from './asteroidsCompetitionHelpers'
import type { PendingAsteroidsSubmission } from './asteroidsCompetitionHelpers'
import '../../index.css'
import './AsteroidsCompetitionPage.css'

export type AsteroidsCompetitionGateway = ArcadeCompetitionGateway & AsteroidsPaidCompetitionGateway & AsteroidsFreeRunGateway

export type AsteroidsCompetitionPageProps = Readonly<{
  account: AccountSummary
  gateway: AsteroidsCompetitionGateway
  onPaidAccountRefresh?: () => void | Promise<void>
}>

type CompetitionPhase = 'lobby' | 'starting-free' | 'starting-paid' | 'playing-free' | 'playing-paid' | 'submitting' | 'submit-failed' | 'result'

type ActiveRun = Readonly<{
  kind: 'free' | 'paid'
  period?: ArcadeCompetitionPaidPeriod
  runId: string
  seedHex: string
}>

type CompetitionResult = Readonly<{
  kind: 'free'
  recordedScore: number
}> | Readonly<{
  kind: 'paid'
  officialScore: number
}>

type PendingStart = Readonly<{ kind: 'free'; idempotencyKey: string }> | Readonly<{ kind: 'paid'; period: ArcadeCompetitionPaidPeriod; idempotencyKey: string }>
type StoredAsteroidsState = Readonly<{ state: 'active'; run: ActiveRun }> | Readonly<{ state: 'submitting'; submission: PendingAsteroidsSubmission }> | Readonly<{ state: 'starting'; start: PendingStart }>

const paidChoices: readonly Readonly<{ period: ArcadeCompetitionPaidPeriod, label: string }>[] = [
  { period: 'daily', label: 'Daily competition · R1' },
  { period: 'weekly', label: 'Weekly competition · R1' },
]

export function AsteroidsCompetitionPage({
  account,
  gateway,
  onPaidAccountRefresh,
}: AsteroidsCompetitionPageProps) {
  const [phase, setPhase] = useState<CompetitionPhase>('lobby')
  const [activeRun, setActiveRun] = useState<ActiveRun | null>(null)
  const [pendingSubmission, setPendingSubmission] = useState<PendingAsteroidsSubmission | null>(null)
  const [result, setResult] = useState<CompetitionResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [leaderboardVersion, setLeaderboardVersion] = useState(0)
  const startingRun = useRef(false)
  const pendingStart = useRef<PendingStart | null>(null)
  const submittingRun = useRef<string | null>(null)
  const isActiveLayout = phase === 'playing-free' || phase === 'playing-paid'

  const refreshPaidAccount = useCallback(async () => {
    try {
      await onPaidAccountRefresh?.()
    } catch {
      setNotice('Your competition action succeeded, but your balance could not be refreshed yet.')
    }
  }, [onPaidAccountRefresh])

  const submitReplay = useCallback(async (submission: PendingAsteroidsSubmission) => {
    if (submittingRun.current === submission.runId) return

    submittingRun.current = submission.runId
    setError(null)
    setPhase('submitting')
    try {
      const completion = submission.kind === 'paid'
        ? await gateway.completeAsteroidsReplay(submission.period, submission.runId, submission.replay)
        : await gateway.completeFreeAsteroidsReplay(submission.runId, submission.replay)
      setResult(submission.kind === 'paid'
        ? { kind: 'paid', officialScore: completion.score }
        : { kind: 'free', recordedScore: completion.score })
      setPendingSubmission(null)
      clearStoredAsteroidsState(account.userId)
      if (submission.kind === 'paid') setLeaderboardVersion((version) => version + 1)
      setPhase('result')
      if (submission.kind === 'paid') void refreshPaidAccount()
    } catch (reason: unknown) {
      setError(friendlyAsteroidsCompetitionError(reason))
      if (requiresFreshAsteroidsRun(reason)) {
        clearStoredAsteroidsState(account.userId)
        setActiveRun(null)
        setPendingSubmission(null)
        setPhase('lobby')
      } else {
        setPhase('submit-failed')
      }
    } finally {
      submittingRun.current = null
    }
  }, [account.userId, gateway, refreshPaidAccount])

  useEffect(() => {
    const stored = readStoredAsteroidsState(account.userId)
    if (stored === null) return
    if (stored.state === 'active') {
      setActiveRun(stored.run)
      setPhase(stored.run.kind === 'paid' ? 'playing-paid' : 'playing-free')
      return
    }
    if (stored.state === 'submitting') {
      setPendingSubmission(stored.submission)
      void submitReplay(stored.submission)
      return
    }

    startingRun.current = true
    pendingStart.current = stored.start
    setPhase(stored.start.kind === 'paid' ? 'starting-paid' : 'starting-free')
    let isCurrent = true

    const activate = (active: ActiveRun) => {
      if (!isCurrent) return
      pendingStart.current = null
      storeAsteroidsState(account.userId, { state: 'active', run: active })
      setActiveRun(active)
      setError(null)
      setPhase(active.kind === 'paid' ? 'playing-paid' : 'playing-free')
      if (active.kind === 'paid') void refreshPaidAccount()
    }
    const recoverStartFailure = (reason: unknown) => {
      if (!isCurrent) return
      setError(friendlyAsteroidsCompetitionError(reason))
      setPhase('lobby')
      startingRun.current = false
    }

    if (stored.start.kind === 'paid') {
      void gateway.startAsteroidsAttempt(stored.start.period, stored.start.idempotencyKey)
        .then((attempt) => activate({ kind: 'paid', period: attempt.period, runId: attempt.runId, seedHex: attempt.seedHex }))
        .catch(recoverStartFailure)
        .finally(() => { if (isCurrent) startingRun.current = false })
    } else {
      void gateway.startFreeAsteroidsRun(stored.start.idempotencyKey)
        .then((run) => activate({ kind: 'free', runId: run.runId, seedHex: run.seedHex }))
        .catch(recoverStartFailure)
        .finally(() => { if (isCurrent) startingRun.current = false })
    }
    return () => { isCurrent = false }
  }, [account.userId, gateway, refreshPaidAccount, submitReplay])

  const beginPaidRun = async (period: ArcadeCompetitionPaidPeriod) => {
    if (phase !== 'lobby' || startingRun.current) return
    if (pendingStart.current !== null && (pendingStart.current.kind !== 'paid' || pendingStart.current.period !== period)) {
      setError('A competition entry is still being restored. Retry that entry before choosing another flight.')
      return
    }

    startingRun.current = true
    setError(null)
    setPhase('starting-paid')
    const start = pendingStart.current ?? { kind: 'paid' as const, period, idempotencyKey: createAsteroidsIdempotencyKey() }
    pendingStart.current = start
    storeAsteroidsState(account.userId, { state: 'starting', start })
    try {
      const attempt = await gateway.startAsteroidsAttempt(start.period, start.idempotencyKey)
      const active: ActiveRun = { kind: 'paid', period: attempt.period, runId: attempt.runId, seedHex: attempt.seedHex }
      pendingStart.current = null
      storeAsteroidsState(account.userId, { state: 'active', run: active })
      setActiveRun(active)
      setPhase('playing-paid')
      void refreshPaidAccount()
    } catch (reason: unknown) {
      setError(friendlyAsteroidsCompetitionError(reason))
      setPhase('lobby')
    } finally {
      startingRun.current = false
    }
  }

  const beginFreeRun = async () => {
    if (phase !== 'lobby' || startingRun.current) return
    if (pendingStart.current !== null && pendingStart.current.kind !== 'free') {
      setError('A competition entry is still being restored. Retry that entry before choosing another flight.')
      return
    }

    startingRun.current = true
    setPhase('starting-free')
    const start = pendingStart.current ?? { kind: 'free' as const, idempotencyKey: createAsteroidsIdempotencyKey() }
    pendingStart.current = start
    storeAsteroidsState(account.userId, { state: 'starting', start })
    try {
      const run = await gateway.startFreeAsteroidsRun(start.idempotencyKey)
      const active: ActiveRun = { kind: 'free', runId: run.runId, seedHex: run.seedHex }
      pendingStart.current = null
      storeAsteroidsState(account.userId, { state: 'active', run: active })
      setError(null)
      setNotice(null)
      setActiveRun(active)
      setPhase('playing-free')
    } catch (reason: unknown) {
      setError(friendlyAsteroidsCompetitionError(reason))
      setPhase('lobby')
    } finally {
      startingRun.current = false
    }
  }

  const completeRun = async (replay: AsteroidsReplayPayload, display: AsteroidsReplayDisplayResult) => {
    const run = activeRun
    if (run === null || phase === 'submitting' || submittingRun.current === run.runId) return

    if (run.kind === 'free') {
      const submission = createPendingFreeAsteroidsSubmission(run.runId, replay, display)
      setPendingSubmission(submission)
      storeAsteroidsState(account.userId, { state: 'submitting', submission })
      void submitReplay(submission)
      return
    }

    const submission = createPendingPaidAsteroidsSubmission(run.period!, run.runId, replay, display)
    setPendingSubmission(submission)
    storeAsteroidsState(account.userId, { state: 'submitting', submission })
    void submitReplay(submission)
  }

  const returnToLobby = () => {
    if (phase === 'playing-paid' || phase === 'submitting' || phase === 'submit-failed' || phase === 'starting-paid' || phase === 'starting-free') return
    setActiveRun(null)
    setPendingSubmission(null)
    setResult(null)
    setError(null)
    setNotice(null)
    setPhase('lobby')
  }

  return (
    <div className={`asteroids-competition-page${isActiveLayout ? ' asteroids-competition-page--active' : ''}`}>
      <main className="asteroids-competition-page__content">
        {!isActiveLayout && (
          <header className="asteroids-competition-page__intro">
            <p className="asteroids-competition-page__eyebrow">Asteroids</p>
            <h1>Competition arena</h1>
            <p>Fly a fresh seeded run, submit your replay, and chase the official leaderboard.</p>
          </header>
        )}

        {(phase === 'lobby' || phase === 'starting-paid' || phase === 'starting-free') && (
          <div className="asteroids-competition-page__lobby-grid">
            <div className="asteroids-competition-page__leaderboard">
              <ArcadeCompetitionLeaderboard key={leaderboardVersion} gameId="asteroids" gateway={gateway} />
            </div>
            <section aria-labelledby="asteroids-play-options" className="asteroids-competition-page__options">
              <h2 id="asteroids-play-options">Choose your flight</h2>
              <p>R1 competition entries add R1 to the displayed jackpot. Casual runs are recorded to your account but do not enter the leaderboard.</p>
              <div className="asteroids-competition-page__choice-grid">
                {paidChoices.map((choice) => (
                  <button
                    className="asteroids-competition-page__choice"
                    disabled={phase === 'starting-paid' || phase === 'starting-free'}
                    key={choice.period}
                    onClick={() => void beginPaidRun(choice.period)}
                    type="button"
                  >
                    {phase === 'starting-paid' ? 'Preparing secure run…' : choice.label}
                  </button>
                ))}
                <button
                  className="asteroids-competition-page__choice asteroids-competition-page__choice--free"
                  disabled={phase === 'starting-paid' || phase === 'starting-free'}
                  onClick={() => void beginFreeRun()}
                  type="button"
                >
                  {phase === 'starting-free' ? 'Preparing recorded run…' : 'Casual run'}
                </button>
              </div>
            </section>
          </div>
        )}

        {(phase === 'playing-free' || phase === 'playing-paid') && activeRun !== null && (
          <section aria-label={activeRun.kind === 'paid' ? 'Competition run' : 'Casual run'} className="asteroids-competition-page__run">
            <AsteroidsReplayPlay key={activeRun.runId} modeLabel={activeRun.kind === 'paid' ? 'Competition' : 'Casual'} onComplete={completeRun} runId={activeRun.runId} seedHex={activeRun.seedHex} />
          </section>
        )}

        {(phase === 'submitting' || phase === 'submit-failed') && pendingSubmission !== null && (
          <section aria-labelledby="asteroids-submission" className="asteroids-competition-page__result">
            <h2 id="asteroids-submission">{phase === 'submitting' ? 'Submitting replay' : 'Replay submission interrupted'}</h2>
            <p>Provisional game score: <strong>{pendingSubmission.display.score}</strong></p>
            {phase === 'submitting'
              ? <p role="status">Submitting your completed replay for official scoring…</p>
              : (
                <>
                  <p>Your completed replay is saved. Retry this exact replay to record its official result.</p>
                  <button onClick={() => void submitReplay(pendingSubmission)} type="button">Retry submission</button>
                </>
              )}
          </section>
        )}

        {phase === 'result' && result !== null && (
          <section aria-labelledby="asteroids-result" className="asteroids-competition-page__result">
            <h2 id="asteroids-result">{result.kind === 'paid' ? 'Official result' : 'Practice complete'}</h2>
            {result.kind === 'paid' && <p>{paidOfficialScoreText(result.officialScore)}</p>}
            {result.kind === 'free' && <p>Recorded practice score: <strong>{result.recordedScore}</strong></p>}
            {result.kind === 'free' && <p>This free-play result is saved to your account and is not leaderboard eligible.</p>}
            <button onClick={returnToLobby} type="button">Return to lobby and play again</button>
          </section>
        )}

        {error !== null && <p className="asteroids-competition-page__error" role="alert">{error}</p>}
        {notice !== null && <p role="status">{notice}</p>}
      </main>
    </div>
  )
}

function asteroidsStateKey(playerId: string): string {
  return `fortuneforge:asteroids:state:${playerId}`
}

function storeAsteroidsState(playerId: string, state: StoredAsteroidsState): void {
  try {
    sessionStorage.setItem(asteroidsStateKey(playerId), JSON.stringify(state))
  } catch {
    // Session storage is a recovery aid only. The server remains authoritative.
  }
}

function clearStoredAsteroidsState(playerId: string): void {
  try {
    sessionStorage.removeItem(asteroidsStateKey(playerId))
  } catch {
    // Session storage is a recovery aid only. The server remains authoritative.
  }
}

function readStoredAsteroidsState(playerId: string): StoredAsteroidsState | null {
  try {
    const encoded = sessionStorage.getItem(asteroidsStateKey(playerId))
    if (encoded === null) return null
    const value: unknown = JSON.parse(encoded)
    const state = asRecord(value)
    if (state?.state === 'starting') {
      const start = readPendingStart(state.start)
      return start === null ? null : { state: 'starting', start }
    }
    if (state?.state === 'active') {
      const run = readActiveRun(state.run)
      return run === null ? null : { state: 'active', run }
    }
    if (state?.state === 'submitting') {
      const submission = readPendingSubmission(state.submission)
      return submission === null ? null : { state: 'submitting', submission }
    }
  } catch {
    // A malformed browser cache must never prevent a player from opening the game.
  }
  clearStoredAsteroidsState(playerId)
  return null
}

function readPendingStart(value: unknown): PendingStart | null {
  const start = asRecord(value)
  if (start === null || !isIdempotencyKey(start.idempotencyKey)) return null
  if (start.kind === 'free') return { kind: 'free', idempotencyKey: start.idempotencyKey }
  if (start.kind === 'paid' && isPaidPeriod(start.period)) {
    return { kind: 'paid', period: start.period, idempotencyKey: start.idempotencyKey }
  }
  return null
}

function readActiveRun(value: unknown): ActiveRun | null {
  const run = asRecord(value)
  if (run === null || !isRunId(run.runId) || !isSeedHex(run.seedHex)) return null
  if (run.kind === 'free') return { kind: 'free', runId: run.runId, seedHex: run.seedHex }
  if (run.kind === 'paid' && isPaidPeriod(run.period)) {
    return { kind: 'paid', period: run.period, runId: run.runId, seedHex: run.seedHex }
  }
  return null
}

function readPendingSubmission(value: unknown): PendingAsteroidsSubmission | null {
  const submission = asRecord(value)
  if (submission === null || !isRunId(submission.runId) || !isReplay(submission.replay) || !isDisplay(submission.display)) return null
  if (submission.kind === 'free') {
    return createPendingFreeAsteroidsSubmission(submission.runId, submission.replay, submission.display)
  }
  if (submission.kind === 'paid' && isPaidPeriod(submission.period)) {
    return createPendingPaidAsteroidsSubmission(submission.period, submission.runId, submission.replay, submission.display)
  }
  return null
}

function isReplay(value: unknown): value is AsteroidsReplayPayload {
  const replay = asRecord(value)
  const totalSteps = replay?.totalSteps
  if (replay === null || !isBoundedInteger(totalSteps, 1, maximumReplaySteps) || !Array.isArray(replay.commands) || replay.commands.length > maximumReplayCommands) return false
  let previousStep = -1
  return replay.commands.every((candidate) => {
    const command = asRecord(candidate)
    if (command === null || !isBoundedInteger(command.step, 0, totalSteps - 1) || !isBoundedInteger(command.input, 0, 15) || command.step <= previousStep) return false
    previousStep = command.step
    return true
  })
}

function isDisplay(value: unknown): value is AsteroidsReplayDisplayResult {
  const display = asRecord(value)
  return display !== null
    && isBoundedInteger(display.score, 0, Number.MAX_SAFE_INTEGER)
    && isBoundedInteger(display.wave, 0, Number.MAX_SAFE_INTEGER)
    && isBoundedInteger(display.lives, 0, Number.MAX_SAFE_INTEGER)
    && (display.reason === 'game-over' || display.reason === 'time-up')
}

function requiresFreshAsteroidsRun(reason: unknown): boolean {
  if (!(reason instanceof ArcadeCompetitionRequestError)) return false
  return reason.status === 400 || reason.status === 409 || reason.status === 410
    || reason.code?.includes('replay') === true
    || reason.code?.includes('run') === true
    || reason.code?.includes('closed') === true
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : null
}

function isPaidPeriod(value: unknown): value is ArcadeCompetitionPaidPeriod {
  return value === 'daily' || value === 'weekly'
}

function isRunId(value: unknown): value is string {
  return typeof value === 'string' && /^[A-Za-z0-9_-]{16,128}$/.test(value)
}

function isSeedHex(value: unknown): value is string {
  return typeof value === 'string' && /^[A-Fa-f0-9]{16}$/.test(value)
}

function isIdempotencyKey(value: unknown): value is string {
  return typeof value === 'string' && /^[a-z0-9-]{16,128}$/.test(value)
}

function isBoundedInteger(value: unknown, minimum: number, maximum: number): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= minimum && value <= maximum
}

import { useCallback, useEffect, useRef, useState } from 'react'
import {
  FlappyReplaySession,
  FlappyRules,
  flappyLevel,
  keyboardFlapIntent,
  maximumFlappyReplayFlaps,
  maximumFlappyReplayTicks,
  pointerFlapIntent,
  type FlappyReplayCompletion as LocalFlappyReplayCompletion,
  type FlappyReplayPayload,
  type FlappyReplaySessionView,
} from '@fortuneforge/games-flappy'
import '@fortuneforge/games-flappy/styles.css'
import { PlayerHeader } from '../../../components/PlayerHeader'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import {
  ArcadeCompetitionRequestError,
  type FlappyFreeRun,
  type FlappyFreeRunGateway,
  type FlappyReplayCompletion,
} from '../../../games/arcade/arcadeCompetitionApi'
import './FlappyFreeRunPage.css'

export type FlappyFreeRunPageProps = Readonly<{
  account: AccountSummary
  gateway: FlappyFreeRunGateway
}>

type Phase = 'lobby' | 'starting' | 'playing' | 'submitting' | 'submit-failed' | 'result'
type PendingStart = Readonly<{ idempotencyKey: string }>
type PendingSubmission = Readonly<{ runId: string; replay: FlappyReplayPayload; display: LocalFlappyReplayCompletion['display'] }>

export function FlappyFreeRunPage({ account, gateway }: FlappyFreeRunPageProps) {
  const [phase, setPhase] = useState<Phase>('lobby')
  const [activeRun, setActiveRun] = useState<FlappyFreeRun | null>(null)
  const [pending, setPending] = useState<PendingSubmission | null>(null)
  const [result, setResult] = useState<FlappyReplayCompletion | null>(null)
  const [error, setError] = useState<string | null>(null)
  const starting = useRef(false)
  const startRequestKey = useRef<string | null>(null)
  const submittingRun = useRef<string | null>(null)

  const beginRun = async () => {
    if (phase !== 'lobby' || starting.current) return
    starting.current = true
    setError(null)
    setPhase('starting')
    const idempotencyKey = startRequestKey.current ?? createFlappyIdempotencyKey()
    startRequestKey.current = idempotencyKey
    storePendingStart(account.userId, { idempotencyKey })
    try {
      const run = await gateway.startFreeFlappyRun(idempotencyKey)
      clearStoredStart(account.userId)
      startRequestKey.current = null
      setActiveRun(run)
      setPhase('playing')
    } catch (reason: unknown) {
      setError(friendlyFlappyError(reason))
      setPhase('lobby')
    } finally {
      starting.current = false
    }
  }

  const submit = useCallback(async (submission: PendingSubmission) => {
    if (submittingRun.current === submission.runId) return
    submittingRun.current = submission.runId
    setError(null)
    setPhase('submitting')
    try {
      const completion = await gateway.completeFreeFlappyReplay(submission.runId, submission.replay)
      setResult(completion)
      setPending(null)
      clearStoredSubmission(account.userId)
      setPhase('result')
    } catch (reason: unknown) {
      setError(friendlyFlappyError(reason))
      if (requiresFreshFlappyRun(reason)) {
        clearStoredSubmission(account.userId)
        setPending(null)
        setActiveRun(null)
        setPhase('lobby')
      } else {
        setPhase('submit-failed')
      }
    } finally {
      submittingRun.current = null
    }
  }, [account.userId, gateway])

  useEffect(() => {
    const storedSubmission = readStoredSubmission(account.userId)
    if (storedSubmission !== null) {
      setPending(storedSubmission)
      void submit(storedSubmission)
      return
    }

    const storedStart = readStoredStart(account.userId)
    if (storedStart === null) return

    let disposed = false
    starting.current = true
    startRequestKey.current = storedStart.idempotencyKey
    setError(null)
    setPhase('starting')
    void gateway.startFreeFlappyRun(storedStart.idempotencyKey).then(run => {
      if (disposed) return
      clearStoredStart(account.userId)
      startRequestKey.current = null
      setActiveRun(run)
      setPhase('playing')
    }).catch((reason: unknown) => {
      if (disposed) return
      setError(friendlyFlappyError(reason))
      setPhase('lobby')
    }).finally(() => {
      if (!disposed) starting.current = false
    })

    return () => { disposed = true }
  }, [account.userId, gateway, submit])

  const completeRun = useCallback((replay: FlappyReplayPayload, display: LocalFlappyReplayCompletion['display']) => {
    const run = activeRun
    if (run === null || submittingRun.current === run.runId) return
    const submission = { runId: run.runId, replay, display }
    setPending(submission)
    storeSubmission(account.userId, submission)
    void submit(submission)
  }, [account.userId, activeRun, submit])

  const returnToLobby = () => {
    if (phase === 'starting' || phase === 'playing' || phase === 'submitting') return
    setActiveRun(null)
    setPending(null)
    setResult(null)
    setError(null)
    setPhase('lobby')
  }

  return <div className="flappy-free-run-page">
    <PlayerHeader account={account} />
    <main className="flappy-free-run-page__content">
      {(phase === 'lobby' || phase === 'starting') && <section className="flappy-free-run-page__lobby">
        <p className="flappy-free-run-page__eyebrow">Classic arcade</p>
        <h1>Flappy</h1>
        <p>Thread the flier through the openings. Every flight is securely recorded to your account.</p>
        <p><strong>Space or left click/tap to flap.</strong> Gravity never stops pulling downward.</p>
        <p>Free play does not enter the jackpot.</p>
        <button disabled={phase === 'starting' || pending !== null} onClick={() => void beginRun()} type="button">
          {phase === 'starting' ? 'Preparing recorded flight…' : 'Start flight'}
        </button>
      </section>}

      {phase === 'playing' && activeRun !== null && <FlappyReplayPlay key={activeRun.runId} run={activeRun} onComplete={completeRun} />}

      {(phase === 'submitting' || phase === 'submit-failed') && pending !== null && <section className="flappy-free-run-page__result" aria-labelledby="flappy-submission">
        <h2 id="flappy-submission">{phase === 'submitting' ? 'Recording flight' : 'Recording interrupted'}</h2>
        <p>Provisional score: <strong>{pending.display.score}</strong></p>
        {phase === 'submitting'
          ? <p role="status">Verifying your flap timing and saving the official result…</p>
          : <>
              <p>Your flight start is saved. Retry this exact replay to store its completed result.</p>
              <button onClick={() => void submit(pending)} type="button">Retry recording</button>
            </>}
      </section>}

      {phase === 'result' && result !== null && <section className="flappy-free-run-page__result" aria-labelledby="flappy-result">
        <h2 id="flappy-result">Flight recorded</h2>
        <p>Official score: <strong>{result.score}</strong></p>
        <p>Your result is saved to your account and is not leaderboard eligible.</p>
        <button onClick={returnToLobby} type="button">Fly again</button>
      </section>}

      {error !== null && <p className="flappy-free-run-page__error" role="alert">{error}</p>}
    </main>
  </div>
}

function FlappyReplayPlay({ run, onComplete }: Readonly<{ run: FlappyFreeRun; onComplete: (replay: FlappyReplayPayload, display: LocalFlappyReplayCompletion['display']) => void }>) {
  const session = useRef(new FlappyReplaySession(run.runId, run.seed)).current
  const [view, setView] = useState<FlappyReplaySessionView>(session.view)
  const [started, setStarted] = useState(false)
  const viewRef = useRef(view)
  const startedRef = useRef(false)
  const queuedFlap = useRef(false)
  const playfield = useRef<HTMLElement | null>(null)
  const onCompleteRef = useRef(onComplete)

  useEffect(() => { onCompleteRef.current = onComplete }, [onComplete])
  useEffect(() => { playfield.current?.focus() }, [])
  useEffect(() => {
    if (!started) return
    const timer = window.setInterval(() => {
      const flap = queuedFlap.current
      queuedFlap.current = false
      const next = session.advanceFrame(flap)
      viewRef.current = next
      setView(next)
      const completion = session.takeCompletion()
      if (completion !== null) onCompleteRef.current(completion.replay, completion.display)
    }, FlappyRules.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [session, started])

  const requestFlap = () => {
    if (viewRef.current.status !== 'running') return
    if (!startedRef.current) {
      startedRef.current = true
      setStarted(true)
    }
    queuedFlap.current = true
  }
  const onKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    const intent = keyboardFlapIntent(event.code, event.repeat, viewRef.current.status === 'running')
    if (intent.preventDefault) event.preventDefault()
    if (intent.flap) requestFlap()
  }
  const onPlayfieldClick = (event: React.MouseEvent<HTMLElement>) => {
    const intent = pointerFlapIntent(event.button, viewRef.current.status === 'running')
    if (intent.preventDefault) event.preventDefault()
    if (intent.flap) {
      playfield.current?.focus()
      requestFlap()
    }
  }
  const game = view.state

  return <section className="flappy-free-run-page__run" aria-label="Recorded Flappy flight">
    <div className="ff-flappy-stats" aria-live="polite"><span>Score <strong>{game.score}</strong></span><span>Best <strong>{game.bestScore}</strong></span><span>Level <strong>{flappyLevel(game.score)}</strong></span></div>
    <p className="flappy-free-run-page__instruction"><strong>Space or left click/tap to flap.</strong> Each press gives one upward flap.</p>
    <section
      aria-label="Flappy playfield"
      className="ff-flappy-playfield"
      onClick={onPlayfieldClick}
      onContextMenu={event => event.preventDefault()}
      onKeyDown={onKeyDown}
      ref={playfield}
      tabIndex={0}
    >
      <svg aria-label="Flier and obstacles" role="img" viewBox={`0 0 ${game.width} ${game.height}`}>
        <defs>
          <linearGradient id="flappy-sky" x2="0" y2="1"><stop stopColor="#2d7a9e" /><stop offset=".56" stopColor="#174462" /><stop offset="1" stopColor="#091a2b" /></linearGradient>
          <linearGradient id="flappy-ground" x2="0" y2="1"><stop stopColor="#c99b4a" /><stop offset="1" stopColor="#5c3519" /></linearGradient>
          <linearGradient id="flappy-bird" x2="0" y2="1"><stop stopColor="#ffe89a" /><stop offset="1" stopColor="#f39d32" /></linearGradient>
        </defs>
        <rect width={game.width} height={game.height} fill="url(#flappy-sky)" />
        <circle className="ff-flappy-sun" cx={game.width * .78} cy={game.height * .17} r={game.height * .09} />
        <path className="ff-flappy-cloud" d={`M${game.width * .08} ${game.height * .2}c11-23 43-23 54 0 20-9 39 4 39 21H${game.width * .03}c0-12 9-21 22-21 9 0 17 4 21 10`} />
        <path className="ff-flappy-cloud ff-flappy-cloud--far" d={`M${game.width * .49} ${game.height * .36}c9-18 33-18 43 0 15-7 30 3 30 17H${game.width * .45}c0-10 8-17 19-17 7 0 13 3 17 8`} />
        {game.obstacles.map(obstacle => <g key={obstacle.id} className="ff-flappy-pipe">
          <rect className="ff-flappy-pipe-shaft" x={obstacle.x} y={0} width={obstacle.width} height={obstacle.gapTop} />
          <rect className="ff-flappy-pipe-rim" x={obstacle.x} y={Math.max(0, obstacle.gapTop - 16)} width={obstacle.width} height={16} />
          <rect className="ff-flappy-pipe-shaft" x={obstacle.x} y={obstacle.gapBottom} width={obstacle.width} height={game.height - obstacle.gapBottom} />
          <rect className="ff-flappy-pipe-rim" x={obstacle.x} y={obstacle.gapBottom} width={obstacle.width} height={16} />
        </g>)}
        <rect className="ff-flappy-ground" x={0} y={game.height - 18} width={game.width} height={18} />
        <g className="ff-flappy-bird-art" transform={`translate(${FlappyRules.birdX} ${game.birdY})`}>
          <ellipse className="ff-flappy-bird-wing" cx={-5} cy={5} rx={10} ry={6} />
          <ellipse className="ff-flappy-bird-body" rx={14} ry={11} />
          <path className="ff-flappy-bird-beak" d="M12 -2 23 2 12 7Z" />
          <circle className="ff-flappy-bird-eye" cx={5} cy={-4} r={3.4} />
          <circle className="ff-flappy-bird-pupil" cx={6} cy={-4} r={1.35} />
        </g>
      </svg>
      {!started && <div className="ff-flappy-overlay"><small>Ready to fly</small><strong>Flap to begin</strong><button onClick={(event) => { event.stopPropagation(); playfield.current?.focus(); requestFlap() }} type="button">Start flight</button></div>}
      {view.status === 'failed' && <div className="ff-flappy-overlay"><small>Flight unavailable</small><strong>{view.error ?? 'The replay limit was reached.'}</strong></div>}
    </section>
  </section>
}

function createFlappyIdempotencyKey(): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `flappy-${random}`
}

function friendlyFlappyError(reason: unknown): string {
  if (reason instanceof ArcadeCompetitionRequestError) {
    if (reason.code === 'arcade-flappy-free-run-conflict') return 'That recorded flight cannot be completed again with different input.'
    if (reason.code === 'arcade-flappy-replay-invalid') return 'The flight replay could not be verified. Please start a new flight.'
    if (reason.status === 401) return 'Your session has ended. Sign in again to record a flight.'
  }
  return 'Flappy could not reach the game service. Please try again.'
}

function requiresFreshFlappyRun(reason: unknown): boolean {
  return reason instanceof ArcadeCompetitionRequestError &&
    (reason.code === 'arcade-flappy-free-run-conflict' || reason.code === 'arcade-flappy-replay-invalid')
}

function storedSubmissionKey(playerId: string): string { return `fortuneforge:flappy:pending:${playerId}` }
function storedStartKey(playerId: string): string { return `fortuneforge:flappy:start:${playerId}` }

function readStoredStart(playerId: string): PendingStart | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(storedStartKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const start = value as Record<string, unknown>
    return isRequestKey(start.idempotencyKey) ? { idempotencyKey: start.idempotencyKey } : null
  } catch { /* session storage is optional */ }
  return null
}

function readStoredSubmission(playerId: string): PendingSubmission | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(storedSubmissionKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const submission = value as Record<string, unknown>
    if (!isNonBlankString(submission.runId) || !isReplay(submission.replay) || !isDisplay(submission.display)) return null
    return { runId: submission.runId, replay: submission.replay, display: submission.display }
  } catch { /* session storage is optional */ }
  return null
}

function isReplay(value: unknown): value is FlappyReplayPayload {
  if (!value || typeof value !== 'object') return false
  const replay = value as Record<string, unknown>
  const totalTicks = replay.totalTicks
  if (typeof totalTicks !== 'number' || !Number.isInteger(totalTicks) || totalTicks < 1 || totalTicks > maximumFlappyReplayTicks || !Array.isArray(replay.flapTicks) || replay.flapTicks.length > maximumFlappyReplayFlaps) return false
  let prior = -1
  return replay.flapTicks.every(tick => {
    const valid = Number.isInteger(tick) && tick >= 0 && tick < totalTicks && tick > prior
    if (valid) prior = tick
    return valid
  })
}

function isDisplay(value: unknown): value is LocalFlappyReplayCompletion['display'] {
  if (!value || typeof value !== 'object') return false
  const display = value as Record<string, unknown>
  const score = display.score
  return typeof score === 'number' && Number.isSafeInteger(score) && score >= 0 && isTerminalPhase(display.phase)
}

function isTerminalPhase(value: unknown): value is LocalFlappyReplayCompletion['display']['phase'] {
  return value === 'obstacle-collision' || value === 'ground-collision' || value === 'ceiling-collision'
}

function isNonBlankString(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0 }
function isRequestKey(value: unknown): value is string { return typeof value === 'string' && /^[A-Za-z0-9_-]{16,128}$/.test(value) }
function storePendingStart(playerId: string, start: PendingStart): void { try { sessionStorage.setItem(storedStartKey(playerId), JSON.stringify(start)) } catch { /* session storage is optional */ } }
function clearStoredStart(playerId: string): void { try { sessionStorage.removeItem(storedStartKey(playerId)) } catch { /* session storage is optional */ } }
function storeSubmission(playerId: string, submission: PendingSubmission): void { try { sessionStorage.setItem(storedSubmissionKey(playerId), JSON.stringify(submission)) } catch { /* session storage is optional */ } }
function clearStoredSubmission(playerId: string): void { try { sessionStorage.removeItem(storedSubmissionKey(playerId)) } catch { /* session storage is optional */ } }

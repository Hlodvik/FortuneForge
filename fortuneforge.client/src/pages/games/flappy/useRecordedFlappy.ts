import { useCallback, useEffect, useRef, useState } from 'react'
import { FlappyReplaySession, FlappyRules, type FlappyReplaySessionView } from '@fortuneforge/games-flappy'
import { ArcadeCompetitionRequestError, type FlappyFreeRunGateway, type FlappyReplayCompletion } from '../../../games/arcade/arcadeCompetitionApi'
import { clearFlappyRecovery, readFlappyRecovery, restoreFlight, writeFlappyRecovery,
  type FlappyRecovery, type FlightSubmission } from './flappyRecovery'

export type RecordedFlappyPhase = 'lobby' | 'starting' | 'start-failed' | 'playing' | 'paused' | 'submitting' | 'submit-failed' | 'result' | 'failed' | 'unavailable'

export function useRecordedFlappy(userId: string, gateway: FlappyFreeRunGateway) {
  const [initial] = useState(() => readFlappyRecovery(userId))
  const initialRecovery = initial.recovery
  const [recovery, setRecovery] = useState<FlappyRecovery | null>(initialRecovery)
  const recoveryRef = useRef(recovery)
  const [initialSession] = useState(() => initialRecovery?.kind === 'flight' ? restoreFlight(initialRecovery.run, initialRecovery.cursor)
    : initialRecovery?.kind === 'submission' && initialRecovery.submission.run ? restoreFlight(initialRecovery.submission.run, initialRecovery.submission.replay) : null)
  const session = useRef<FlappyReplaySession | null>(initialSession)
  const flaps = useRef<number[]>(initialRecovery?.kind === 'flight' ? [...initialRecovery.cursor.flapTicks] : [])
  const [view, setView] = useState<FlappyReplaySessionView | null>(session.current?.view ?? null)
  const [phase, setPhase] = useState<RecordedFlappyPhase>(initial.error ? 'unavailable' : initialRecovery?.kind === 'flight' ? 'paused'
    : initialRecovery?.kind === 'submission' ? 'submit-failed' : initialRecovery?.kind === 'start' ? 'start-failed' : 'lobby')
  const phaseRef = useRef(phase)
  const [error, setError] = useState<string | null>(initial.error)
  const [result, setResult] = useState<FlappyReplayCompletion | null>(null)
  const [sessionBest, setSessionBest] = useState<number | null>(null)
  const busy = useRef<AbortController | null>(null)
  const alive = useRef(true)
  const queuedFlap = useRef(false)
  const move = useCallback((next: RecordedFlappyPhase) => { phaseRef.current = next; setPhase(next) }, [])
  const retain = useCallback((next: FlappyRecovery) => {
    recoveryRef.current = next
    setRecovery(next)
    return writeFlappyRecovery(userId, next)
  }, [userId])

  useEffect(() => {
    alive.current = true
    return () => { alive.current = false; busy.current?.abort(); busy.current = null }
  }, [])

  const submit = useCallback(async (submission: FlightSubmission) => {
    if (busy.current) return
    const storageError = retain({ kind: 'submission', submission })
    if (storageError) { setError(storageError); move('submit-failed'); return }
    const request = new AbortController()
    busy.current = request
    setError(null); move('submitting')
    try {
      const completion = await gateway.completeFreeFlappyReplay(submission.runId, submission.replay, request.signal)
      if (!alive.current || busy.current !== request) return
      setResult(completion)
      setSessionBest(best => Math.max(best ?? 0, completion.score))
      const clearError = clearFlappyRecovery(userId)
      setError(clearError)
      recoveryRef.current = null; setRecovery(null)
      move('result')
    } catch (reason) {
      if (!alive.current || busy.current !== request) return
      setError(friendlyError(reason))
      // A terminal rejection does not erase the exact saved replay; it can still be retried or explicitly cleared.
      move('submit-failed')
    } finally { if (busy.current === request) busy.current = null }
  }, [gateway, move, retain, userId])

  const start = useCallback(async () => {
    if (busy.current || !['lobby', 'result', 'start-failed'].includes(phaseRef.current)) return
    const prior = recoveryRef.current
    const idempotencyKey = prior?.kind === 'start' ? prior.idempotencyKey : `flappy-${crypto.randomUUID().replaceAll('-', '')}`
    const storageError = retain({ kind: 'start', idempotencyKey })
    if (storageError) { setError(storageError); return }
    const request = new AbortController()
    busy.current = request
    setError(null); setResult(null); move('starting')
    try {
      const run = await gateway.startFreeFlappyRun(idempotencyKey, request.signal)
      if (!alive.current || busy.current !== request) return
      session.current = new FlappyReplaySession(run.runId, run.seed)
      flaps.current = []; queuedFlap.current = true
      setView(session.current.view)
      const saveError = retain({ kind: 'flight', run, cursor: { totalTicks: 0, flapTicks: [] } })
      setError(saveError); move(saveError ? 'paused' : 'playing')
    } catch (reason) {
      if (!alive.current || busy.current !== request) return
      setError(friendlyError(reason)); move('start-failed')
    } finally { if (busy.current === request) busy.current = null }
  }, [gateway, move, retain])

  const pause = useCallback(() => {
    if (phaseRef.current !== 'playing') return
    queuedFlap.current = false
    move('paused')
  }, [move])
  const resume = useCallback((flap = false) => {
    if (phaseRef.current !== 'paused' || !session.current || session.current.view.status !== 'running') return
    const pending = recoveryRef.current
    if (pending?.kind !== 'flight') return
    const storageError = retain(pending)
    if (storageError) { setError(storageError); return }
    queuedFlap.current = flap || session.current.view.state.tick === 0
    setError(null); move('playing')
  }, [move, retain])
  const flap = useCallback(() => {
    if (phaseRef.current === 'playing' && session.current?.view.status === 'running') queuedFlap.current = true
    else if (phaseRef.current === 'paused') resume(true)
  }, [resume])

  useEffect(() => {
    if (phase !== 'playing') return
    // A start response may arrive after the tab/window was left, before the live listeners exist.
    if (document.hidden || !document.hasFocus()) { pause(); return }
    const timer = window.setInterval(() => {
      if (phaseRef.current !== 'playing') return
      const current = session.current, stored = recoveryRef.current
      if (!current || stored?.kind !== 'flight') return
      const flap = queuedFlap.current; queuedFlap.current = false
      if (flap) flaps.current.push(current.view.state.tick)
      const next = current.advanceFrame(flap)
      setView(next)
      if (next.status === 'failed') {
        setError('The recording limit was reached. This flight was not recorded.'); move('failed'); return
      }
      const completion = current.takeCompletion()
      if (completion) { void submit({ run: stored.run, runId: stored.run.runId, ...completion }); return }
      const storageError = retain({ kind: 'flight', run: stored.run, cursor: { totalTicks: next.state.tick, flapTicks: [...flaps.current] } })
      if (storageError) { setError(storageError); pause() }
    }, FlappyRules.tickMilliseconds)
    const hide = () => { if (document.hidden) pause() }
    window.addEventListener('blur', pause); document.addEventListener('visibilitychange', hide)
    return () => { clearInterval(timer); window.removeEventListener('blur', pause); document.removeEventListener('visibilitychange', hide) }
  }, [move, pause, phase, retain, submit])

  const retry = () => {
    const saved = recoveryRef.current
    if (saved?.kind === 'submission') void submit(saved.submission)
    else if (saved?.kind === 'start') void start()
  }
  const discard = () => {
    if (busy.current || phaseRef.current === 'playing') return
    const storageError = clearFlappyRecovery(userId)
    if (storageError) { setError(storageError); return }
    recoveryRef.current = null; setRecovery(null)
    session.current = null; setView(null); flaps.current = []; queuedFlap.current = false
    setError(null); setResult(null); move('lobby')
  }
  return { phase, view, recovery, error, result, sessionBest, start, pause, resume, flap, retry, discard }
}

function friendlyError(reason: unknown) {
  if (reason instanceof ArcadeCompetitionRequestError) {
    if (reason.status === 401) return 'Your session has ended. Sign in again to record this flight.'
    if (reason.code === 'arcade-flappy-free-run-conflict') return 'The service has different input for this flight. Your saved replay is still available.'
    if (reason.code === 'arcade-flappy-replay-invalid') return 'The service could not verify this flight. Your saved replay is still available.'
    if (reason.status === 404) return 'The service could not find this flight. Your saved input is still available.'
  }
  return 'The game service could not be reached. Retry the saved flight.'
}

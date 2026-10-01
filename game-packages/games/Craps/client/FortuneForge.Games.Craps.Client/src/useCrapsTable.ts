import { useEffect, useRef, useState } from 'react'
import { CrapsGatewayError, type CrapsExtraBetRequest, type CrapsGateway, type CrapsRound, type CrapsStatus } from './contracts'
import { validStake } from './crapsPresentation'

/** Read back known hands after uncertain writes; this server does not support replay. */
export function useCrapsTable(gateway: CrapsGateway, playerId?: string, isYourTurn = true, onRoundChange?: (round: CrapsRound | null) => void) {
  const [status, setStatus] = useState<CrapsStatus | null>(null)
  const [round, setRound] = useState<CrapsRound | null>(null)
  const [pendingRound, setPendingRound] = useState<CrapsRound | null>(null)
  const [busy, setBusy] = useState(true)
  const [motion, setMotion] = useState<'idle' | 'rolling' | 'settling'>('idle')
  const [error, setError] = useState<string | null>(null)
  const [recovery, setRecovery] = useState<'ready' | 'failed'>('ready')
  const [attempt, setAttempt] = useState(0)
  const current = useRef<CrapsRound | null>(null)
  const scope = useRef<string | null>(null)
  const failedId = useRef<string | null>(null)
  const gate = useRef(true)
  const alive = useRef(false)
  const request = useRef<AbortController | null>(null)
  const identity = useRef({ gateway, playerId })
  const mode = useRef<string | null>(null)
  const callback = useRef(onRoundChange)
  const turn = useRef(isYourTurn)
  callback.current = onRoundChange; turn.current = isYourTurn

  function accept(next: CrapsRound, signal: AbortSignal) {
    if (signal.aborted || !alive.current) return
    current.current = next; store(scope.current, next.roundId); setRound(next); callback.current?.(next)
  }
  async function reveal(next: CrapsRound, signal: AbortSignal, animate: boolean) {
    if (signal.aborted || !alive.current) return
    if (animate) {
      setPendingRound(next); setMotion('settling')
      const reduced = typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
      if (!reduced) await new Promise<void>(resolve => window.setTimeout(resolve, 780))
    }
    if (!signal.aborted) { accept(next, signal); setPendingRound(null) }
  }
  function expire() {
    store(scope.current, null); current.current = null; failedId.current = null
    setRound(null); setRecovery('ready'); callback.current?.(null)
    setError('This practice hand expired. Place a new bet.')
  }

  useEffect(() => {
    alive.current = true
    if (identity.current.gateway !== gateway || identity.current.playerId !== playerId) {
      identity.current = { gateway, playerId }; current.current = null; failedId.current = null; scope.current = null; mode.current = null
      setRound(null); setStatus(null); setPendingRound(null); setRecovery('ready'); setMotion('idle')
    }
    gate.current = true; setBusy(true); setError(null)
    const controller = new AbortController()
    void (async () => {
      try {
        const next = await gateway.getStatus(controller.signal)
        if (controller.signal.aborted) return
        if (mode.current !== null && mode.current !== next.mode) { current.current = null; failedId.current = null; setRound(null); setRecovery('ready') }
        mode.current = next.mode; setStatus(next)
        scope.current = playerId && gateway.getRound ? encodeURIComponent(playerId) + ':' + encodeURIComponent(next.mode) : null
        const id = failedId.current ?? read(scope.current)
        if (id && !gateway.getRound) { setRecovery('failed'); setError('Hand could not be refreshed. Reopen the table before continuing.'); return }
        if (id && gateway.getRound) {
          failedId.current = id
          try { accept(await gateway.getRound(id, controller.signal), controller.signal) }
          catch (reason) {
            if (controller.signal.aborted) return
            if (reason instanceof CrapsGatewayError && reason.status === 404) expire()
            else { setRecovery('failed'); setError('Hand could not be restored. Retry before rolling or betting.'); return }
          }
        }
        if (controller.signal.aborted) return
        failedId.current = null; setRecovery('ready')
        if (!next.available) setError('This table is temporarily unavailable.')
      } catch (reason) { if (!controller.signal.aborted) { setStatus(null); setError(message(reason)) } }
      finally { if (!controller.signal.aborted) { gate.current = false; setBusy(false) } }
    })()
    return () => { alive.current = false; controller.abort(); request.current?.abort() }
  }, [gateway, playerId, attempt])

  async function reconcile(id: string, signal: AbortSignal, animate: boolean, reason: unknown) {
    failedId.current = id
    if (!gateway.getRound) { setRecovery('failed'); setError('Hand could not be refreshed. Reopen the table before continuing.'); return }
    try {
      const refreshed = await gateway.getRound(id, signal)
      await reveal(refreshed, signal, animate)
      if (signal.aborted) return
      failedId.current = null; setRecovery('ready')
      setError(reason instanceof CrapsGatewayError && reason.status >= 400 && reason.status < 500 ? reason.message : 'Connection interrupted. Hand refreshed; check your bets.')
    } catch (readError) {
      if (signal.aborted) return
      if (readError instanceof CrapsGatewayError && readError.status === 404) expire()
      else { setRecovery('failed'); setError('Hand could not be restored. Retry before rolling or betting.') }
    }
  }
  async function run(operation: (signal: AbortSignal) => Promise<CrapsRound>, id: string | null, animate = false) {
    if (gate.current || recovery !== 'ready' || !status?.available || !turn.current) return
    gate.current = true; setBusy(true); setError(null)
    const controller = new AbortController(); request.current = controller
    if (animate) setMotion('rolling')
    try { await reveal(await operation(controller.signal), controller.signal, animate) }
    catch (reason) {
      if (controller.signal.aborted) return
      if (id) await reconcile(id, controller.signal, animate, reason)
      else setError(reason instanceof CrapsGatewayError && reason.status >= 400 && reason.status < 500 ? reason.message : 'Bet response lost. Start a new practice hand when ready.')
    } finally { if (alive.current && !controller.signal.aborted) { setPendingRound(null); setMotion('idle'); setBusy(false); gate.current = false } }
  }
  function start(stake: number, extras: readonly CrapsExtraBetRequest[] = []) {
    if (current.current && current.current.phase !== 'resolved' || !validStake(stake, status) || extras.some(bet => !validStake(bet.stake, status))) return
    void run(signal => gateway.startRound(stake, signal, extras), null)
  }
  function roll() {
    const hand = current.current
    if (!hand || hand.phase === 'resolved') return
    void run(signal => gateway.roll(hand.roundId, signal), hand.roundId, true)
  }
  function placeOdds(stake: number) {
    const hand = current.current
    if (!hand || hand.phase !== 'point' || !gateway.placeOdds || hand.extraBets?.some(bet => bet.kind === 'odds' && !bet.resolved) || !validStake(stake, status)) return
    void run(signal => gateway.placeOdds!(hand.roundId, stake, signal), hand.roundId)
  }
  function clear() {
    if (gate.current || recovery !== 'ready' || !turn.current || current.current?.phase !== 'resolved') return
    current.current = null; store(scope.current, null); setRound(null); setError(null); callback.current?.(null)
  }
  return { status, round, pendingRound, busy, motion, error, recovery, start, roll, placeOdds, clear, retry: () => setAttempt(value => value + 1) }
}
function message(reason: unknown) { return reason instanceof CrapsGatewayError ? reason.message : 'Craps is unavailable. Check again.' }
function key(scope: string) { return 'fortuneforge:craps:round:' + scope }
function store(scope: string | null, id: string | null) { if (!scope) return; try { if (id) sessionStorage.setItem(key(scope), id); else sessionStorage.removeItem(key(scope)) } catch { /* optional storage */ } }
function read(scope: string | null) { if (!scope) return null; try { const id = sessionStorage.getItem(key(scope)); return id && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) ? id : null } catch { return null } }

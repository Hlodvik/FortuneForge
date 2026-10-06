import { useEffect, useRef, useState } from 'react'
import { RouletteGatewayError, type RouletteBet, type RouletteGateway, type RouletteRound, type RouletteStatus } from './contracts'

type Request = Omit<RouletteBet, 'betIndex' | 'playerId'>
export type HistoryEntry = Readonly<{ roundId: string; pocket: number }>
const spinTime = 1100

/** Writes are never replayed: the server has no idempotency contract. */
export function useRouletteTable(gateway: RouletteGateway, playerId?: string) {
  const [status, setStatus] = useState<RouletteStatus | null>(null)
  const [round, setRound] = useState<RouletteRound | null>(null)
  const [history, setHistory] = useState<readonly HistoryEntry[]>([])
  const [busy, setBusy] = useState(true)
  const [spinning, setSpinning] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [recovery, setRecovery] = useState<'ready' | 'failed'>('ready')
  const [attempt, setAttempt] = useState(0)
  const current = useRef<RouletteRound | null>(null)
  const scope = useRef<string | null>(null)
  const gate = useRef(true)
  const alive = useRef(false)
  const request = useRef<AbortController | null>(null)
  const failedId = useRef<string | null>(null)
  const identity = useRef({ gateway, playerId })
  const lastMode = useRef<string | null>(null)

  function accept(next: RouletteRound, signal: AbortSignal) {
    if (signal.aborted || !alive.current) return false
    current.current = next
    storeRound(scope.current, next.roundId)
    setRound(next)
    const acceptedScope = scope.current
    if (next.phase === 'settled' && next.winningPocket !== null) {
      setHistory(previous => {
        const historyId = next.roundNumber ? `${next.roundId}:${next.roundNumber}` : next.roundId
        const entries = previous.some(entry => entry.roundId === historyId) ? previous : [{ roundId: historyId, pocket: next.winningPocket! }, ...previous].slice(0, 20)
        storeHistory(acceptedScope, entries)
        return entries
      })
    }
    return true
  }

  useEffect(() => {
    alive.current = true
    if (identity.current.gateway !== gateway || identity.current.playerId !== playerId) {
      identity.current = { gateway, playerId }; current.current = null; failedId.current = null; scope.current = null; lastMode.current = null
      setRound(null); setStatus(null); setHistory([]); setRecovery('ready'); setSpinning(false)
    }
    gate.current = true
    setBusy(true); setError(null)
    const controller = new AbortController()
    void (async () => {
      try {
        const next = await gateway.getStatus(controller.signal)
        if (controller.signal.aborted) return
        setStatus(next)
        if (lastMode.current !== null && lastMode.current !== next.mode) { current.current = null; failedId.current = null; setRound(null); setRecovery('ready') }
        lastMode.current = next.mode
        scope.current = playerId ? encodeURIComponent(playerId) + ':' + encodeURIComponent(next.mode) : null
        setHistory(readHistory(scope.current))
        const id = failedId.current ?? readRound(scope.current)
        if (id) {
          failedId.current = id
          try { const restored = await gateway.getRound(id, controller.signal); if (!controller.signal.aborted) accept(restored, controller.signal) }
          catch (reason) {
            if (controller.signal.aborted) return
            if (reason instanceof RouletteGatewayError && reason.status === 404) { storeRound(scope.current, null); failedId.current = null; current.current = null; setRound(null); setError('This practice table expired. Open a new table.') }
            else { setRecovery('failed'); setError('Your table could not be restored. Retry restoration before placing more chips.'); return }
          }
        }
        if (controller.signal.aborted) return
        failedId.current = null; setRecovery('ready')
        if (!next.available) setError('This table is temporarily unavailable.')
      } catch (reason) { if (!controller.signal.aborted) { setStatus(null); setError(messageForError(reason)) } }
      finally { if (!controller.signal.aborted) { gate.current = false; setBusy(false) } }
    })()
    return () => { alive.current = false; controller.abort(); request.current?.abort() }
  }, [gateway, playerId, attempt])

  useEffect(() => {
    if (!round?.players || recovery !== 'ready') return
    const controller = new AbortController()
    const refresh = window.setInterval(() => {
      if (gate.current || !current.current) return
      void gateway.getRound(current.current.roundId, controller.signal)
        .then(next => accept(next, controller.signal))
        .catch(() => { /* the normal recovery path handles the next player action */ })
    }, 1_500)
    return () => { window.clearInterval(refresh); controller.abort() }
  }, [gateway, round?.roundId, round?.players, recovery])

  async function reconcile(id: string, signal: AbortSignal, reason: unknown, revealAt = 0) {
    failedId.current = id
    try {
      const restored = await gateway.getRound(id, signal)
      const delay = revealAt - performance.now()
      if (delay > 0) await new Promise<void>(resolve => window.setTimeout(resolve, delay))
      if (signal.aborted) return
      accept(restored, signal); failedId.current = null; setRecovery('ready')
      setError(restored.phase === 'settled' ? null : reason instanceof RouletteGatewayError && reason.status >= 400 && reason.status < 500 ? reason.message : 'Connection interrupted. Table refreshed; check your chips.')
    } catch (readError) {
      if (signal.aborted) return
      if (readError instanceof RouletteGatewayError && readError.status === 404) {
        storeRound(scope.current, null); failedId.current = null; current.current = null; setRound(null); setRecovery('ready'); setError('This practice table expired. Open a new table.')
      } else { setRecovery('failed'); setError('Your table could not be restored. Retry restoration before placing more chips.') }
    }
  }

  async function run(operation: (signal: AbortSignal) => Promise<void>, animate = false, recoveryId: () => string | null = () => current.current?.roundId ?? null) {
    if (gate.current || recovery !== 'ready' || !status?.available) return
    gate.current = true; setBusy(true); setError(null)
    const controller = new AbortController(); request.current = controller
    const started = performance.now()
    if (animate) setSpinning(true)
    try { await operation(controller.signal) }
    catch (reason) {
      if (controller.signal.aborted) return
      const id = recoveryId()
      if (id) await reconcile(id, controller.signal, reason, animate ? started + spinTime : 0)
      else setError(messageForError(reason))
    } finally {
      const remaining = animate ? spinTime - (performance.now() - started) : 0
      if (remaining > 0) await new Promise<void>(resolve => window.setTimeout(resolve, remaining))
      if (alive.current && !controller.signal.aborted) { setSpinning(false); setBusy(false); gate.current = false }
    }
  }

  function open(bets: readonly Request[] = []) {
    let openedId: string | null = null
    void run(async signal => {
      const fresh = await gateway.openRound(signal); if (!accept(fresh, signal)) return; openedId = fresh.roundId
      for (const bet of bets) { if (!accept(await gateway.placeBet(current.current!.roundId, bet, signal), signal)) return }
    }, false, () => openedId)
  }
  function add(bets: readonly Request[]) {
    if (current.current?.phase !== 'open') return
    void run(async signal => { for (const bet of bets) { if (!accept(await gateway.placeBet(current.current!.roundId, bet, signal), signal)) return } })
  }
  function remove(index: number) {
    if (current.current?.phase !== 'open') return
    void run(async signal => { accept(await gateway.removeBet(current.current!.roundId, index, signal), signal) })
  }
  function clear() {
    if (current.current?.phase !== 'open') return
    void run(async signal => { accept(await gateway.clearBets(current.current!.roundId, signal), signal) })
  }
  function spin() {
    if (current.current?.phase !== 'open' || !current.current.bets.length) return
    // Keep the old table/result visible until the minimum reveal finishes.
    void run(async signal => {
      const started = performance.now()
      const next = await gateway.spin(current.current!.roundId, signal)
      const remaining = spinTime - (performance.now() - started)
      if (remaining > 0) await new Promise<void>(resolve => window.setTimeout(resolve, remaining))
      accept(next, signal)
    }, true)
  }
  return { status, round, history, busy, spinning, error, recovery, open, add, remove, clear, spin, retry: () => setAttempt(value => value + 1) }
}

function messageForError(reason: unknown) { return reason instanceof RouletteGatewayError ? reason.message : 'The Roulette table is unavailable. Please retry.' }
function storageKey(scope: string, part: string) { return 'fortuneforge:roulette:' + part + ':' + scope }
function storeRound(scope: string | null, id: string | null) { if (!scope) return; try { if (id) sessionStorage.setItem(storageKey(scope, 'round'), id); else sessionStorage.removeItem(storageKey(scope, 'round')) } catch { /* optional storage */ } }
function readRound(scope: string | null) { if (!scope) return null; try { const id = sessionStorage.getItem(storageKey(scope, 'round')); return id && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) ? id : null } catch { return null } }
function readHistory(scope: string | null): readonly HistoryEntry[] { if (!scope) return []; try { const value: unknown = JSON.parse(localStorage.getItem(storageKey(scope, 'history')) ?? '[]'); return Array.isArray(value) ? value.filter((entry: unknown): entry is HistoryEntry => typeof entry === 'object' && entry !== null && 'roundId' in entry && typeof entry.roundId === 'string' && 'pocket' in entry && Number.isInteger(entry.pocket) && Number(entry.pocket) >= 0 && Number(entry.pocket) <= 36).slice(0, 20) : [] } catch { return [] } }
function storeHistory(scope: string | null, history: readonly HistoryEntry[]) { if (!scope) return; try { localStorage.setItem(storageKey(scope, 'history'), JSON.stringify(history)) } catch { /* optional storage */ } }

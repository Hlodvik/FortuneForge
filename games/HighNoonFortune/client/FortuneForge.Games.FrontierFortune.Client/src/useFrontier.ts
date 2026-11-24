import { useCallback, useEffect, useRef, useState } from 'react'
import { readFrontier, recoverFrontierFailure, sendFrontier } from './api.js'
import { FRONTIER_RULES } from './rules.js'
import { settledFrontierWin, type FrontierWinPresentation } from './winPresentation.js'
import type { FrontierCommand, FrontierFortuneGameProps, FrontierState } from './types.js'

const operationId = () => globalThis.crypto.randomUUID()
const emptyRows = () => Array<number | null>(FRONTIER_RULES.columns).fill(null)

export function useFrontier({ authenticatedFetch, onSpinStateChange, onAccountChange }: FrontierFortuneGameProps) {
  const [state, setState] = useState<FrontierState | null>(null)
  const [confirmedBalanceCents, setConfirmedBalanceCents] = useState<number | null>(null)
  const [busyKind, setBusyKind] = useState<FrontierCommand['kind'] | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retryCommand, setRetryCommand] = useState<FrontierCommand | null>(null)
  const [wagerPoints, setWagerPoints] = useState(FRONTIER_RULES.minWagerPoints)
  const [selectedRows, setSelectedRows] = useState(emptyRows)
  const [held, setHeld] = useState(Array<boolean>(FRONTIER_RULES.columns).fill(false))
  const [auto, setAuto] = useState(false)
  const [reducedMotion, setReducedMotion] = useState(false)
  const [win, setWin] = useState<FrontierWinPresentation | null>(null)
  const clearedWinRevision = useRef<number | null>(null)
  const latest = useRef<FrontierState | null>(null)
  const locked = useRef(false)
  const mounted = useRef(true)
  const callbacks = useRef({ onSpinStateChange, onAccountChange })
  callbacks.current = { onSpinStateChange, onAccountChange }

  const accept = useCallback((next: FrontierState, presentWin = true) => {
    const before = latest.current
    latest.current = next
    if (!mounted.current) return
    setState(next)
    setConfirmedBalanceCents(next.balanceCents)
    if (!presentWin) clearedWinRevision.current = Math.max(clearedWinRevision.current ?? -1, next.revision)
    setWin(presentWin ? settledFrontierWin(next, clearedWinRevision.current) : null)
    const round = next.handRound
    if (round && round.phase !== 'complete' || next.ante.ready || next.pendingOperation) setAuto(false)
    if (!before || before.revision !== next.revision) {
      setSelectedRows(emptyRows())
      setHeld(round?.hand.map(card => card.held) ?? Array<boolean>(FRONTIER_RULES.columns).fill(false))
    }
    callbacks.current.onAccountChange?.(next.balanceCents)
  }, [])

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true); setError(null); setAuto(false)
    try {
      const next = await readFrontier(authenticatedFetch, signal)
      if (signal?.aborted || !mounted.current) return
      accept(next); setRetryCommand(next.pendingOperation)
      if (next.pendingOperation) setError('An earlier action is awaitin’ recovery. Settle it before ridin’ on.')
    } catch (cause) {
      if (!signal?.aborted && mounted.current) setError(cause instanceof Error ? cause.message : 'The trail is quiet. Try again.')
    } finally { if (!signal?.aborted && mounted.current) setLoading(false) }
  }, [accept, authenticatedFetch])
  useEffect(() => {
    mounted.current = true
    const abort = new AbortController(); void load(abort.signal)
    return () => { mounted.current = false; abort.abort(); callbacks.current.onSpinStateChange?.(false) }
  }, [load])
  useEffect(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)')
    const update = () => setReducedMotion(query.matches)
    update(); query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])

  const execute = useCallback(async (command: FrontierCommand) => {
    if (locked.current || !latest.current) return
    locked.current = true; setBusyKind(command.kind); setError(null)
    if (command.kind === 'spin' || command.kind === 'deal-in' || command.kind === 'deal') {
      clearedWinRevision.current = latest.current.revision
      setWin(null)
    }
    if (command.kind !== 'spin') setAuto(false)
    callbacks.current.onSpinStateChange?.(true)
    try {
      const duration = command.kind === 'confirm' ? 120 : reducedMotion ? 100 : auto ? 420 : 900
      const [next] = await Promise.all([sendFrontier(authenticatedFetch, command), new Promise(resolve => window.setTimeout(resolve, duration))])
      accept(next); if (mounted.current) setRetryCommand(null)
    } catch (cause) {
      setAuto(false)
      const recovery = await recoverFrontierFailure(cause, command, authenticatedFetch)
      if (!mounted.current) return
      if (recovery.state) accept(recovery.state, false)
      if (recovery.requiresReload) { latest.current = null; setState(null) }
      setError(recovery.message); setRetryCommand(recovery.retryCommand)
    } finally {
      locked.current = false; callbacks.current.onSpinStateChange?.(false)
      if (mounted.current) setBusyKind(null)
    }
  }, [accept, authenticatedFetch, auto, reducedMotion])

  const activeHand = state?.handRound && state.handRound.phase !== 'complete' ? state.handRound : null
  const canConfirm = activeHand?.phase === 'choose' && activeHand.hand.every((card, index) => card.held || selectedRows[index] !== null)
  const primary = useCallback(() => {
    const current = latest.current
    if (!current || locked.current || loading || retryCommand) return
    const round = current.handRound
    if (round && round.phase !== 'complete') {
      if (round.phase === 'choose') {
        if (!round.hand.every((card, index) => card.held || selectedRows[index] !== null)) return
        void execute({ operationId: operationId(), kind: 'confirm', expectedRevision: current.revision, roundId: round.roundId,
          selectedRows: round.hand.map((card, index) => card.held ? null : selectedRows[index]) })
      } else void execute({ operationId: operationId(), kind: 'deal', expectedRevision: current.revision, roundId: round.roundId, held: [...held] })
      return
    }
    if (current.ante.ready) { void execute({ operationId: operationId(), kind: 'deal-in', expectedRevision: current.revision }); return }
    if (current.balanceCents < wagerPoints * current.wagering.pointValueInCents) { setAuto(false); setError('Choose a smaller wager, partner.'); return }
    void execute({ operationId: operationId(), kind: 'spin', wagerPoints })
  }, [execute, held, loading, retryCommand, selectedRows, wagerPoints])

  useEffect(() => {
    if (!auto || busyKind || loading || error || retryCommand || activeHand || state?.ante.ready) return
    const timer = window.setTimeout(primary, 320)
    return () => window.clearTimeout(timer)
  }, [activeHand, auto, busyKind, error, loading, primary, retryCommand, state?.ante.ready, state?.revision])

  const selectCard = useCallback((column: number, row: number) => {
    if (!activeHand || activeHand.phase !== 'choose' || activeHand.hand[column]?.held || busyKind || retryCommand) return
    setAuto(false); setSelectedRows(previous => previous.map((value, index) => index === column ? row : value))
  }, [activeHand, busyKind, retryCommand])
  const toggleHold = useCallback((column: number) => {
    if (!activeHand || activeHand.phase !== 'hold' || busyKind || retryCommand) return
    setAuto(false); setHeld(previous => previous.map((value, index) => index === column ? !value : value))
  }, [activeHand, busyKind, retryCommand])
  const changeWager = useCallback((points: number) => { setAuto(false); setWagerPoints(points) }, [])
  const setAutoSpinning = useCallback((value: boolean) => { setAuto(Boolean(value && !activeHand && !state?.ante.ready && !retryCommand && !loading)) }, [activeHand, loading, retryCommand, state?.ante.ready])
  return { state, confirmedBalanceCents, loading, error, retryCommand, busy: busyKind !== null, busyKind, reducedMotion, win,
    activeHand, canConfirm, selectedRows, held, wagerPoints, auto, primary, selectCard, toggleHold, changeWager, setAutoSpinning,
    retry: () => retryCommand ? void execute(retryCommand) : void load(), stopAuto: () => setAuto(false) }
}

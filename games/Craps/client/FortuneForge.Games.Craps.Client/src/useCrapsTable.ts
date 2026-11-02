import { useEffect, useRef, useState } from 'react'
import { CrapsGatewayError, type CrapsActionContext, type CrapsExtraBetKind, type CrapsExtraBetRequest, type CrapsGateway, type CrapsRound, type CrapsStatus } from './contracts'
import { validStake } from './crapsPresentation'

type PendingAction =
  | { kind: 'start'; operationId: string; stake: number; extras: readonly CrapsExtraBetRequest[] }
  | { kind: 'roll'; operationId: string; roundId: string; context?: CrapsActionContext }
  | { kind: 'odds'; operationId: string; roundId: string; stake: number; context?: CrapsActionContext }
  | { kind: 'wager'; operationId: string; roundId: string; wagerKind: CrapsExtraBetKind; stake: number; context: CrapsActionContext }

/** Preserve paid actions until the server confirms them, including across page reloads. */
export function useCrapsTable(gateway: CrapsGateway, playerId?: string, isYourTurn = true, onRoundChange?: (round: CrapsRound | null) => void, requirePaid = false) {
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
    if (requirePaid && !validWallet(next.walletBalanceCents)) throw new CrapsGatewayError('The paid table returned no wallet balance.')
    if (validWallet(next.walletBalanceCents)) setStatus(previous => previous ? { ...previous, walletBalanceCents: next.walletBalanceCents } : previous)
    current.current = next; store(scope.current, next.roundId); setRound(next); callback.current?.(next)
  }
  async function refreshBalance(signal: AbortSignal) {
    if (!requirePaid) return
    const fresh = await gateway.getStatus(signal)
    if (fresh.mode !== 'paid-pass-line' || !validWallet(fresh.walletBalanceCents))
      throw new CrapsGatewayError('The paid Craps balance is unavailable. Try again shortly.')
    if (!signal.aborted && alive.current) setStatus(fresh)
  }
  async function finishConfirmed(signal: AbortSignal, refreshHand = false) {
    clearPending(scope.current)
    try {
      if (refreshHand && requirePaid && current.current && gateway.getRound) accept(await gateway.getRound(current.current.roundId, signal), signal)
      await refreshBalance(signal)
    }
    catch (reason) {
      if (signal.aborted) return
      setRecovery('failed'); setError(message(reason) + ' Check again before playing.')
    }
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
    setError('This hand expired. Check your game history before placing another bet.')
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
        if (requirePaid && (next.mode !== 'paid-pass-line' || !validWallet(next.walletBalanceCents) || !gateway.getActiveRound))
          throw new CrapsGatewayError('The paid Craps table is unavailable. Try again shortly.')
        if (mode.current !== null && mode.current !== next.mode) { current.current = null; failedId.current = null; setRound(null); setRecovery('ready') }
        mode.current = next.mode; setStatus(next)
        scope.current = playerId && gateway.getRound ? encodeURIComponent(playerId) + ':' + encodeURIComponent(next.mode) : null
        const pending = readPending(scope.current)
        if (pending === 'invalid') {
          setRecovery('failed'); setError('A pending wager could not be restored safely. Contact support before playing again.'); return
        }
        let acceptedActive = false
        if (pending) {
          try {
            const recovered = await dispatch(pending, controller.signal)
            accept(recovered, controller.signal)
            if (requirePaid && gateway.getRound) accept(await gateway.getRound(recovered.roundId, controller.signal), controller.signal)
            clearPending(scope.current)
          } catch (reason) {
            if (controller.signal.aborted) return
            if (definiteRejection(reason)) {
              clearPending(scope.current)
              const refreshed = pending.kind === 'start' ? await gateway.getActiveRound?.(controller.signal) : await gateway.getRound?.(pending.roundId, controller.signal)
              if (refreshed) accept(refreshed, controller.signal)
              await refreshBalance(controller.signal)
              setRecovery('ready'); setError(reason.message + ' The table was refreshed; check your bets.'); return
            }
            setRecovery('failed'); setError(message(reason) + ' Retry to confirm the wager before playing again.'); return
          }
        } else if (next.mode === 'paid-pass-line' && gateway.getActiveRound) {
          const active = await gateway.getActiveRound(controller.signal)
          if (active) { accept(active, controller.signal); acceptedActive = true }
        }
        const id = failedId.current ?? read(scope.current)
        if (id && !gateway.getRound) { setRecovery('failed'); setError('Hand could not be refreshed. Reopen the table before continuing.'); return }
        if (id && gateway.getRound && (!requirePaid || !pending && !acceptedActive)) {
          failedId.current = id
          try { accept(await gateway.getRound(id, controller.signal), controller.signal) }
          catch (reason) {
            if (controller.signal.aborted) return
            if (reason instanceof CrapsGatewayError && reason.status === 404) expire()
            else { setRecovery('failed'); setError('Hand could not be restored. Retry before rolling or betting.'); return }
          }
        }
        if (requirePaid && current.current) await refreshBalance(controller.signal)
        if (controller.signal.aborted) return
        failedId.current = null; setRecovery('ready')
        if (!next.available) setError('This table is temporarily unavailable.')
      } catch (reason) { if (!controller.signal.aborted) { setStatus(null); setError(message(reason)) } }
      finally { if (!controller.signal.aborted) { gate.current = false; setBusy(false) } }
    })()
    return () => { alive.current = false; controller.abort(); request.current?.abort() }
  }, [gateway, playerId, attempt, requirePaid])

  function dispatch(command: PendingAction, signal: AbortSignal): Promise<CrapsRound> {
    switch (command.kind) {
      case 'start': return gateway.startRound(command.stake, signal, command.extras, command.operationId)
      case 'roll': return gateway.roll(command.roundId, signal, command.operationId, command.context)
      case 'odds': return gateway.placeOdds!(command.roundId, command.stake, signal, command.operationId, command.context)
      case 'wager': {
        if (!gateway.setWager) throw new CrapsGatewayError('Wager adjustments are unavailable. Refresh the table.', 'craps-adjustment-unavailable', 400)
        return gateway.setWager(command.roundId, command.wagerKind, command.stake, signal, command.operationId, command.context)
      }
    }
  }

  async function reconcile(id: string, signal: AbortSignal, animate: boolean, reason: unknown) {
    failedId.current = id
    if (!gateway.getRound) { setRecovery('failed'); setError('Hand could not be refreshed. Reopen the table before continuing.'); return }
    try {
      const refreshed = await gateway.getRound(id, signal)
      await reveal(refreshed, signal, animate)
      if (signal.aborted) return
      failedId.current = null; setRecovery('ready')
      await refreshBalance(signal)
      setError(reason instanceof CrapsGatewayError && reason.status >= 400 && reason.status < 500 ? reason.message : 'Connection interrupted. Hand refreshed; check your bets.')
    } catch (readError) {
      if (signal.aborted) return
      if (readError instanceof CrapsGatewayError && readError.status === 404) expire()
      else { setRecovery('failed'); setError('Hand could not be restored. Retry before rolling or betting.') }
    }
  }
  async function run(command: PendingAction, id: string | null, animate = false) {
    if (gate.current || recovery !== 'ready' || !status?.available || !turn.current) return
    if (requirePaid && !writePending(scope.current, command)) {
      setRecovery('failed'); setError('Browser storage is unavailable. This paid action cannot be confirmed safely.'); return
    }
    gate.current = true; setBusy(true); setError(null)
    const controller = new AbortController(); request.current = controller
    if (animate) setMotion('rolling')
    try {
      await reveal(await dispatch(command, controller.signal), controller.signal, animate)
      await finishConfirmed(controller.signal)
    }
    catch (reason) {
      if (controller.signal.aborted) return
      if (!requirePaid) {
        if (id) await reconcile(id, controller.signal, animate, reason)
        else setError(reason instanceof CrapsGatewayError && reason.status >= 400 && reason.status < 500 ? reason.message : 'Bet response lost. Check your hand before placing another bet.')
        return
      }
      if (definiteRejection(reason)) {
        clearPending(scope.current)
        if (command.kind === 'start' && reason.status === 409 && reason.code === 'craps-round-active') {
          try {
            const active = await gateway.getActiveRound?.(controller.signal)
            if (controller.signal.aborted) return
            if (!active) throw new CrapsGatewayError('The active Craps hand is unavailable.')
            accept(active, controller.signal)
            failedId.current = null; setRecovery('ready')
            setError('Your existing Craps hand was restored. Check your bets before rolling.')
          } catch {
            if (controller.signal.aborted) return
            setRecovery('failed'); setError('Your active Craps hand could not be restored. Check again before rolling or betting.')
          }
        } else if (id) await reconcile(id, controller.signal, animate, reason)
        else setError(reason.message)
      } else {
        try {
          await reveal(await dispatch(command, controller.signal), controller.signal, animate)
          await finishConfirmed(controller.signal, true)
        } catch (retryError) {
          if (controller.signal.aborted) return
          if (definiteRejection(retryError)) {
            clearPending(scope.current)
            if (id) await reconcile(id, controller.signal, animate, retryError)
            else { setError(retryError.message); await refreshBalance(controller.signal) }
            return
          }
          setRecovery('failed')
          setError('The wager result could not be confirmed. Check again to restore this action before playing.')
          if (id && gateway.getRound) {
            try { accept(await gateway.getRound(id, controller.signal), controller.signal) } catch { /* Retain the pending action. */ }
          }
          void retryError
        }
      }
    } finally { if (alive.current && !controller.signal.aborted) { setPendingRound(null); setMotion('idle'); setBusy(false); gate.current = false } }
  }
  function start(stake: number, extras: readonly CrapsExtraBetRequest[] = []) {
    if (current.current && current.current.phase !== 'resolved' || !validStake(stake, status) || extras.some(bet => !validStake(bet.stake, status))) return
    void run({ kind: 'start', operationId: crypto.randomUUID(), stake, extras }, null)
  }
  function roll() {
    const hand = current.current
    if (!hand || hand.phase === 'resolved') return
    const context = actionContext(hand)
    if (requirePaid && !context) { setError('Refresh the table before rolling. The hand revision is unavailable.'); setRecovery('failed'); return }
    void run({ kind: 'roll', operationId: crypto.randomUUID(), roundId: hand.roundId, context }, hand.roundId, true)
  }
  function placeOdds(stake: number) {
    const hand = current.current
    if (status?.mode === 'paid-pass-line' && gateway.setWager && hand?.revision !== undefined) { setWager('odds', stake); return }
    if (!hand || hand.phase !== 'point' || !gateway.placeOdds || hand.extraBets?.some(bet => bet.kind === 'odds' && !bet.resolved) || !validStake(stake, status)) return
    const context = actionContext(hand)
    if (requirePaid && !context) { setError('Refresh the table before changing odds. The hand revision is unavailable.'); setRecovery('failed'); return }
    void run({ kind: 'odds', operationId: crypto.randomUUID(), roundId: hand.roundId, stake, context }, hand.roundId)
  }
  function setWager(kind: CrapsExtraBetKind, stake: number) {
    const hand = current.current
    if (status?.mode !== 'paid-pass-line' || !hand || hand.phase !== 'point' || !gateway.setWager || !['odds', 'field', 'any-seven', 'any-craps'].includes(kind) || stake !== 0 && !validStake(stake, status)) return
    const context = actionContext(hand)
    if (!context) { setError('Refresh the table before changing bets. The hand revision is unavailable.'); return }
    void run({ kind: 'wager', operationId: crypto.randomUUID(), roundId: hand.roundId, wagerKind: kind, stake, context }, hand.roundId)
  }
  function clear() {
    if (gate.current || recovery !== 'ready' || !turn.current || current.current?.phase !== 'resolved') return
    current.current = null; store(scope.current, null); setRound(null); setError(null); callback.current?.(null)
  }
  return { status, round, pendingRound, busy, motion, error, recovery, start, roll, placeOdds, setWager, clear, retry: () => setAttempt(value => value + 1) }
}
function message(reason: unknown) { return reason instanceof CrapsGatewayError ? reason.message : 'Craps is unavailable. Check again.' }
function definiteRejection(reason: unknown): reason is CrapsGatewayError {
  return reason instanceof CrapsGatewayError && [400, 401, 403, 404, 409, 422, 429].includes(reason.status)
}
function pendingKey(scope: string) { return 'fortuneforge:craps:pending:' + scope }
function validWallet(value: unknown): value is number { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 }
function writePending(scope: string | null, command: PendingAction): boolean {
  if (!scope) return false
  try { sessionStorage.setItem(pendingKey(scope), JSON.stringify(command)); return true } catch { return false }
}
function readPending(scope: string | null): PendingAction | 'invalid' | null {
  if (!scope) return null
  try {
    const raw = sessionStorage.getItem(pendingKey(scope))
    if (!raw) return null
    const value: unknown = JSON.parse(raw)
    if (!value || typeof value !== 'object') return 'invalid'
    const command = value as Partial<PendingAction>
    if (typeof command.operationId !== 'string' || !uuid(command.operationId)) return 'invalid'
    if (command.kind === 'start' && wager(command.stake) && Array.isArray(command.extras) && command.extras.every(extra =>
      extra && typeof extra === 'object' && ['field', 'any-seven', 'any-craps'].includes(extra.kind) && wager(extra.stake))) return command as PendingAction
    if (command.kind === 'roll' && typeof command.roundId === 'string' && uuid(command.roundId) && (command.context === undefined || validContext(command.context))) return command as PendingAction
    if (command.kind === 'odds' && typeof command.roundId === 'string' && uuid(command.roundId) && wager(command.stake) && (command.context === undefined || validContext(command.context))) return command as PendingAction
    if (command.kind === 'wager' && typeof command.roundId === 'string' && uuid(command.roundId) && typeof command.wagerKind === 'string' && ['odds', 'field', 'any-seven', 'any-craps'].includes(command.wagerKind)
      && (command.stake === 0 || wager(command.stake)) && validContext(command.context)) return command as PendingAction
    return 'invalid'
  } catch { return 'invalid' }
}
function uuid(value: string) { return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) }
function actionContext(hand: CrapsRound): CrapsActionContext | undefined { return Number.isSafeInteger(hand.revision) && hand.revision! >= 0 ? { expectedRevision: hand.revision!, expectedRollNumber: hand.rolls.length + 1 } : undefined }
function validContext(value: unknown): value is CrapsActionContext {
  if (!value || typeof value !== 'object') return false
  const context = value as CrapsActionContext
  return Number.isSafeInteger(context.expectedRevision) && context.expectedRevision >= 0 && Number.isSafeInteger(context.expectedRollNumber) && context.expectedRollNumber > 0
}
function wager(value: unknown) { return typeof value === 'number' && Number.isFinite(value) && value > 0 && Math.round(value * 100) === value * 100 }
function clearPending(scope: string | null): void { if (!scope) return; try { sessionStorage.removeItem(pendingKey(scope)) } catch { /* No reliable storage to clear. */ } }
function key(scope: string) { return 'fortuneforge:craps:round:' + scope }
function store(scope: string | null, id: string | null) { if (!scope) return; try { if (id) sessionStorage.setItem(key(scope), id); else sessionStorage.removeItem(key(scope)) } catch { /* optional storage */ } }
function read(scope: string | null) { if (!scope) return null; try { const id = sessionStorage.getItem(key(scope)); return id && uuid(id) ? id : null } catch { return null } }

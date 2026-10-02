import { useEffect, useRef, useState } from 'react'
import { LiarsDiceGatewayError, type LiarsDiceBid, type LiarsDiceGateway, type LiarsDiceMatch, type LiarsDiceStatus } from './contracts'
import { appendRoundHistory, validBid, type LiarsDiceHistoryEntry } from './liarsDiceHelpers'

/** Writes are never replayed: known private matches can be read after an uncertain response. */
export function useLiarsDiceTable(gateway: LiarsDiceGateway, playerId?: string) {
  const [status, setStatus] = useState<LiarsDiceStatus | null>(null)
  const [match, setMatch] = useState<LiarsDiceMatch | null>(null)
  const [pendingMatch, setPendingMatch] = useState<LiarsDiceMatch | null>(null)
  const [history, setHistory] = useState<readonly LiarsDiceHistoryEntry[]>([])
  const [busy, setBusy] = useState(true)
  const [rolling, setRolling] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [recovery, setRecovery] = useState<'ready' | 'failed'>('ready')
  const [attempt, setAttempt] = useState(0)
  const current = useRef<LiarsDiceMatch | null>(null)
  const gate = useRef(true), alive = useRef(false)
  const scope = useRef<string | null>(null), failedId = useRef<string | null>(null), mode = useRef<string | null>(null)
  const request = useRef<AbortController | null>(null)
  const identity = useRef({ gateway, playerId })
  function accept(next: LiarsDiceMatch, signal: AbortSignal) {
    if (signal.aborted || !alive.current) return
    const fresh = current.current?.matchId !== next.matchId
    current.current = next; store(scope.current, next.matchId); setMatch(next)
    setHistory(items => appendRoundHistory(fresh ? [] : items, next))
  }
  async function reveal(next: LiarsDiceMatch, signal: AbortSignal, animate: boolean) {
    if (signal.aborted || !alive.current) return
    store(scope.current, next.matchId)
    if (animate) setPendingMatch(next)
    if (animate && typeof window !== 'undefined' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches)
      await new Promise<void>(resolve => window.setTimeout(resolve, 890))
    accept(next, signal); if (!signal.aborted) setPendingMatch(null)
  }
  function expire() {
    store(scope.current, null); current.current = null; failedId.current = null
    setMatch(null); setHistory([]); setRecovery('ready'); setError('Practice match expired. Start a new match.')
  }
  useEffect(() => {
    alive.current = true
    if (identity.current.gateway !== gateway || identity.current.playerId !== playerId) {
      identity.current = { gateway, playerId }; current.current = null; failedId.current = null; mode.current = null; scope.current = null
      setMatch(null); setHistory([]); setStatus(null); setRecovery('ready'); setRolling(false); setPendingMatch(null)
    }
    gate.current = true; setBusy(true); setError(null)
    const controller = new AbortController()
    void (async () => {
      try {
        const next = await gateway.getStatus(controller.signal)
        if (controller.signal.aborted) return
        if (mode.current !== null && mode.current !== next.mode) {
          current.current = null; failedId.current = null; setMatch(null); setHistory([]); setRecovery('ready')
        }
        mode.current = next.mode; setStatus(next)
        scope.current = playerId && gateway.getMatch ? encodeURIComponent(playerId) + ':' + encodeURIComponent(next.mode) : null
        const id = failedId.current ?? read(scope.current)
        if (id) {
          failedId.current = id
          try {
            if (!gateway.getMatch) throw new Error('No recovery read')
            accept(await gateway.getMatch(id, controller.signal), controller.signal)
          } catch (reason) {
            if (controller.signal.aborted) return
            if (reason instanceof LiarsDiceGatewayError && reason.status === 404) expire()
            else { setRecovery('failed'); setError('Match not restored. Check again before playing.'); return }
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
    try {
      if (!gateway.getMatch) throw new Error('No recovery read')
      await reveal(await gateway.getMatch(id, signal), signal, animate)
      if (signal.aborted) return
      failedId.current = null; setRecovery('ready')
      setError(reason instanceof LiarsDiceGatewayError && reason.status >= 400 && reason.status < 500 ? reason.message : 'Connection interrupted. Match refreshed; check the turn.')
    } catch (readError) {
      if (signal.aborted) return
      if (readError instanceof LiarsDiceGatewayError && readError.status === 404) expire()
      else { setRecovery('failed'); setError('Match not restored. Check again before playing.') }
    }
  }
  async function run(operation: (signal: AbortSignal) => Promise<LiarsDiceMatch>, id: string | null, animate = false) {
    if (gate.current || recovery !== 'ready' || !status?.available || identity.current.gateway !== gateway || identity.current.playerId !== playerId) return
    gate.current = true; setBusy(true); setError(null)
    const controller = new AbortController(); request.current = controller
    if (animate) setRolling(true)
    if (!id) { current.current = null; store(scope.current, null); setMatch(null); setHistory([]) }
    try { await reveal(await operation(controller.signal), controller.signal, animate) }
    catch (reason) {
      if (controller.signal.aborted) return
      if (id) await reconcile(id, controller.signal, animate, reason)
      else setError(reason instanceof LiarsDiceGatewayError && reason.status >= 400 && reason.status < 500 ? reason.message : 'Match response lost. Start a new practice match.')
    } finally { if (alive.current && !controller.signal.aborted) { setPendingMatch(null); setRolling(false); setBusy(false); gate.current = false } }
  }
  function humanTurn(hand: LiarsDiceMatch) {
    return hand.phase === 'bidding' && !hand.winner && hand.players.some(p => p.id === hand.currentPlayerId && p.isHuman && p.active)
  }
  function start(dicePerPlayer: number) {
    if (!Number.isSafeInteger(dicePerPlayer) || dicePerPlayer < 1) return
    void run(signal => gateway.startMatch({ dicePerPlayer }, signal), null, true)
  }
  function bid(draft: LiarsDiceBid) {
    const hand = current.current
    if (!hand || !humanTurn(hand) || !validBid(draft, hand.currentBid, hand.totalDice)) return
    void run(signal => gateway.bid(hand.matchId, draft, signal), hand.matchId)
  }
  function call(kind: 'liar' | 'spot-on') {
    const hand = current.current
    if (!hand || !humanTurn(hand) || !hand.currentBid || kind === 'spot-on' && !gateway.spotOn) return
    void run(signal => kind === 'spot-on' ? gateway.spotOn!(hand.matchId, signal) : gateway.challenge(hand.matchId, signal), hand.matchId)
  }
  function nextRound() {
    const hand = current.current
    if (!hand || hand.phase !== 'resolved' || hand.winner) return
    void run(signal => gateway.nextRound(hand.matchId, undefined, signal), hand.matchId, true)
  }
  function advance() {
    const hand = current.current
    if (!hand || hand.phase !== 'bidding' || hand.winner || humanTurn(hand)) return
    void run(signal => gateway.advance(hand.matchId, signal), hand.matchId)
  }
  return { status, match, pendingMatch, history, busy, rolling, error, recovery, start, bid, call, nextRound, advance,
    retry: () => { if (!gate.current) setAttempt(value => value + 1) } }
}
function message(reason: unknown) { return reason instanceof LiarsDiceGatewayError ? reason.message : 'Liar’s Dice is unavailable. Check again.' }
function key(scope: string) { return 'fortuneforge:liars-dice:match:' + scope }
function store(scope: string | null, id: string | null) { if (!scope) return; try { if (id) sessionStorage.setItem(key(scope), id); else sessionStorage.removeItem(key(scope)) } catch { /* optional storage */ } }
function read(scope: string | null) { if (!scope) return null; try { const id = sessionStorage.getItem(key(scope)); return id && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) ? id : null } catch { return null } }

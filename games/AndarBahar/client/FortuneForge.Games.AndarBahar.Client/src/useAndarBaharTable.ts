import { useEffect, useRef, useState } from 'react'
import { AndarBaharGatewayError, type AndarBaharGateway, type AndarBaharRound, type AndarBaharSide, type AndarBaharStatus } from './contracts'
import { chipsFor, clearPending, readHistory, readPending, readStorage, sumChips, writePending, writeStorage, type ChipValue, type HistoryItem, type PendingWager } from './andarBaharSession'
import { playAndarBaharAudio, stopAndarBaharAudio } from './andarBaharAudio'

type Phase = 'loading' | 'betting' | 'submitting' | 'dealing' | 'result' | 'recover'
const failure = (e: unknown) => e instanceof AndarBaharGatewayError ? e.message : 'The connection was interrupted. Restore your deal to confirm the result.'
const rejected = (e: unknown) => e instanceof AndarBaharGatewayError && (e.status === 400 || e.code === 'insufficient-slot-credits' || e.code === 'andar-bahar-insufficient-credits')

export function useAndarBaharTable(gateway: AndarBaharGateway, playerId: string, onBalanceChange?: (balance: number) => void) {
  const [status, setStatus] = useState<AndarBaharStatus | null>(null)
  const [round, setRound] = useState<AndarBaharRound | null>(null)
  const [phase, setPhase] = useState<Phase>('loading')
  const [side, setSide] = useState<AndarBaharSide>('andar')
  const [chips, setChips] = useState<readonly ChipValue[]>([])
  const [selectedChip, setSelectedChip] = useState<ChipValue>(5)
  const [visible, setVisible] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState('Choose a chip, then choose your side')
  const [history, setHistory] = useState<HistoryItem[]>([])
  const [resultOpen, setResultOpen] = useState(false)
  const [muted, setMuted] = useState(() => readStorage('fortuneforge:andar-bahar:muted') === 'true')
  const [attempt, setAttempt] = useState(0)
  const pending = useRef<PendingWager | null>(null), locked = useRef(true), scope = useRef(0), mounted = useRef(true)
  const wagerGeneration = useRef(0)
  const onBalance = useRef(onBalanceChange); onBalance.current = onBalanceChange
  const mute = useRef(muted); mute.current = muted
  const storageKey = `fortuneforge:andar-bahar:pending:${playerId}`
  const historyKey = `fortuneforge:andar-bahar:history:${playerId}`
  const stake = sumChips(chips)
  const editable = (phase === 'betting' || phase === 'result') && status?.available === true
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; stopAndarBaharAudio() } }, [])

  function record(next: AndarBaharRound) {
    setHistory(current => {
      if (current.some(item => item.id === next.roundId)) return current
      const items = [...current, { id: next.roundId, winner: next.winner }].slice(-30)
      writeStorage(historyKey, JSON.stringify(items)); return items
    })
  }
  function finish(next: AndarBaharRound) {
    setVisible(next.deals.length); setPhase('result'); setResultOpen(true)
    clearPending(storageKey, pending.current?.key); pending.current = null; locked.current = false
    setStatus(current => current ? { ...current, balance: next.balance } : current)
    setNotice(`${next.winner === 'andar' ? 'Andar' : 'Bahar'} matches the Joker on card ${next.deals.length}`)
    onBalance.current?.(next.balance); record(next); playAndarBaharAudio('result', mute.current)
  }
  useEffect(() => {
    scope.current++; const abort = new AbortController()
    locked.current = true; setPhase('loading'); setStatus(null); setRound(null); setChips([]); setError(null); setResultOpen(false)
    pending.current = readPending(storageKey); setHistory(readHistory(historyKey))
    void (async () => {
      try {
        const nextStatus = await gateway.getStatus(abort.signal)
        if (abort.signal.aborted) return
        setStatus(nextStatus)
        if (pending.current) {
          const request = pending.current; setChips(request.chips); setSide(request.side); setNotice('Restoring your previous deal…')
          const restored = await gateway.createRound(request.side, request.stake, { idempotencyKey: request.key, signal: abort.signal })
          if (abort.signal.aborted) return
          setRound(restored); setVisible(0); setPhase('dealing')
        } else {
          const latest = await gateway.getLatestRound(abort.signal)
          if (abort.signal.aborted) return
          if (latest) {
            setRound(latest); setVisible(latest.deals.length); setSide(latest.side); setChips(chipsFor(latest.stake)); record(latest)
            setStatus({ ...nextStatus, balance: latest.balance }); setNotice('Last result restored · place a chip for a new round')
          } else setNotice('Choose a chip, then choose your side')
          setPhase(latest ? 'result' : 'betting'); locked.current = false
        }
      } catch (e) {
        if (abort.signal.aborted) return
        if (pending.current && !rejected(e)) { setPhase('recover'); setError(failure(e)); setNotice('Your deal is pending · restore it before playing again') }
        else {
          clearPending(storageKey, pending.current?.key); pending.current = null; locked.current = false; setPhase('betting')
          setError(e instanceof AndarBaharGatewayError ? e.message : 'Could not connect to the table. Reconnect to load your balance.'); setNotice('Reconnect to the table')
        }
      }
    })()
    return () => abort.abort()
  }, [gateway, playerId, attempt])

  useEffect(() => {
    if (phase !== 'dealing' || !round) return
    setVisible(0)
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) { finish(round); return }
    let number = 0, timer = 0
    const next = () => {
      number++; setVisible(number); playAndarBaharAudio('deal', mute.current)
      if (number === round.deals.length) timer = window.setTimeout(() => finish(round), 650)
      else timer = window.setTimeout(next, 320)
    }
    playAndarBaharAudio('deal', mute.current); timer = window.setTimeout(next, 700)
    return () => window.clearTimeout(timer)
  }, [phase, round?.roundId])

  function place(nextSide: AndarBaharSide) {
    if (!editable || locked.current || !status) return
    const next = [...(phase === 'result' || side !== nextSide ? [] : chips), selectedChip]
    if (sumChips(next) > status.balance) { setError('Your balance is too low for this chip.'); return }
    if (sumChips(next) > status.maximumStake) { setError(`The maximum bet is ${status.maximumStake.toFixed(2)}.`); return }
    setSide(nextSide); setChips(next); setRound(null); setVisible(0); setResultOpen(false); setPhase('betting'); setError(null)
    setNotice('Add chips or deal when you’re ready'); playAndarBaharAudio('chip', muted)
  }
  async function submit(restoring = false) {
    if (locked.current && !restoring || !status?.available) return
    const request = restoring ? pending.current : { key: `andar-bahar-${crypto.randomUUID()}`, side, stake, chips }
    if (!request || request.stake <= 0) return
    if (!restoring && !writePending(storageKey, request)) { setError('Allow site storage so your wager can be restored after a refresh.'); return }
    locked.current = true; pending.current = request
    const requestScope = scope.current
    const generation = ++wagerGeneration.current
    setPhase('submitting'); setError(null); setRound(null); setVisible(0); setResultOpen(false); setNotice(restoring ? 'Restoring your deal…' : 'Confirming your wager…')
    try {
      const next = await gateway.createRound(request.side, request.stake, { idempotencyKey: request.key })
      if (!mounted.current || requestScope !== scope.current) return
      setRound(next); setPhase('dealing'); setNotice('Follow the cards · first match wins')
    } catch (e) {
      if (!mounted.current || requestScope !== scope.current) return
      if (rejected(e)) {
        clearPending(storageKey, request.key); pending.current = null; locked.current = false; setPhase('betting')
        void gateway.getStatus().then(next => { if (mounted.current && requestScope === scope.current && generation === wagerGeneration.current) setStatus(next) }).catch(() => undefined)
      } else { setPhase('recover'); setNotice('Your deal is pending · restore it before playing again') }
      setError(failure(e))
    }
  }
  function clear() {
    if (!editable || locked.current) return
    setChips([]); setRound(null); setVisible(0); setPhase('betting'); setError(null); setResultOpen(false); setNotice('Choose a chip, then choose your side')
  }
  function undo() { if (editable && phase === 'betting') { setChips(current => current.slice(0, -1)); setError(null) } }
  function rebet() {
    if (!editable || !round || !status) return
    if (round.stake > status.balance) { setError('Your balance is too low to repeat this bet.'); return }
    setChips(chipsFor(round.stake)); setSide(round.side); setRound(null); setVisible(0); setResultOpen(false); setPhase('betting'); setError(null); setNotice('Previous bet repeated · deal when you’re ready')
    playAndarBaharAudio('chip', muted)
  }
  return {
    status, round, phase, side, chips, selectedChip, visible, error, notice, history, resultOpen, muted, stake, editable,
    place, submit, clear, undo, rebet,
    chooseChip: (chip: ChipValue) => { if (editable) { setSelectedChip(chip); playAndarBaharAudio('chip', muted) } },
    skip: () => { if (round && phase === 'dealing') finish(round) },
    closeResult: () => setResultOpen(false),
    reconnect: () => setAttempt(value => value + 1),
    toggleMuted: () => { if (!muted) stopAndarBaharAudio(); setMuted(!muted); writeStorage('fortuneforge:andar-bahar:muted', String(!muted)) }
  }
}

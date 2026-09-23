import { useEffect, useRef, useState } from 'react'
import { KenoGatewayError, type KenoGateway, type KenoRound, type KenoStatus } from './contracts'
import { HttpKenoGateway } from './httpKenoGateway'
import './keno.css'

export type KenoGameProps = Readonly<{ gateway?: KenoGateway; playerId?: string; initialSelection?: readonly number[]; onBalanceChange?: (balance: number) => void }>

const maximumSelections = 10
const kenoNumbers = Array.from({ length: 80 }, (_, index) => index + 1)
const defaultGateway = new HttpKenoGateway()
type StoredPendingDraw = Readonly<{ idempotencyKey: string; numbers: readonly number[] }>

export function KenoGame({ gateway = defaultGateway, playerId, initialSelection = [], onBalanceChange }: KenoGameProps) {
  const [status, setStatus] = useState<KenoStatus | null>(null)
  const [selectedNumbers, setSelectedNumbers] = useState(() =>
    [...new Set(initialSelection.filter(number => Number.isInteger(number) && number >= 1 && number <= 80))]
      .slice(0, maximumSelections).sort((left, right) => left - right))
  const [round, setRound] = useState<KenoRound | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const roundRequestKey = useRef<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setTableUnavailable(!nextStatus.available)
      setError(nextStatus.available ? null : 'This table is temporarily unavailable. Please choose another game.')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      const unavailable = isTableUnavailable(reason)
      setTableUnavailable(unavailable)
      setError(unavailable ? 'This table is temporarily unavailable. Please choose another game.' : messageForError(reason))
    })
    return () => controller.abort()
  }, [gateway, statusLoadAttempt])

  useEffect(() => {
    const pendingDraw = playerId ? readPendingDraw(playerId) : null
    if (!pendingDraw) {
      setRecovery('ready')
      return undefined
    }
    const controller = new AbortController()
    setRecovery('recovering')
    void gateway.createRound({ ticket: { numbers: pendingDraw.numbers } }, {
      signal: controller.signal,
      idempotencyKey: pendingDraw.idempotencyKey,
    }).then(nextRound => {
      clearPendingDraw(playerId)
      setRound(nextRound)
      onBalanceChange?.(nextRound.balance)
      setRecovery('ready')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      setRecovery('failed')
      setError('Your pending Keno draw could not be restored. Retry restoration before changing this ticket.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  const isFull = selectedNumbers.length === maximumSelections
  const canPlay = !busy && recovery === 'ready' && status?.available === true && selectedNumbers.length > 0
  const drawnNumbers = round ? new Set(round.draw.numbers) : null
  const ticketNumbers = round ? new Set(round.ticket.numbers) : null

  const toggleNumber = (number: number) => {
    roundRequestKey.current = null
    clearPendingDraw(playerId)
    setRound(null)
    setError(null)
    setSelectedNumbers(current => current.includes(number)
      ? current.filter(selected => selected !== number)
      : current.length < maximumSelections ? [...current, number].sort((left, right) => left - right) : current)
  }

  const clearSelection = () => {
    roundRequestKey.current = null
    clearPendingDraw(playerId)
    setSelectedNumbers([])
    setRound(null)
    setError(null)
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  const play = () => {
    if (busy || recovery !== 'ready' || !status?.available) return
    if (selectedNumbers.length === 0) {
      setError('Select from 1 to 10 Keno numbers before playing.')
      return
    }

    setBusy(true)
    setError(null)
    setRound(null)
    const idempotencyKey = roundRequestKey.current ??= createRequestKey()
    storePendingDraw(playerId, { idempotencyKey, numbers: selectedNumbers })
    void gateway.createRound({ ticket: { numbers: selectedNumbers } }, { idempotencyKey })
      .then(nextRound => { roundRequestKey.current = null; clearPendingDraw(playerId); setRound(nextRound); onBalanceChange?.(nextRound.balance) })
      .catch(reason => setError(messageForError(reason)))
      .finally(() => setBusy(false))
  }

  return <main className="ff-keno" aria-busy={busy}>
    <header className="ff-keno__header">
      <span className="ff-keno__eyebrow">Choose your lucky numbers</span>
      <h1>Keno</h1>
      <p>Select from 1 to 10 numbers on the 80-number board. Free play draws are saved to your account.</p>
      <small>{status?.available ? 'Keno ready' : status ? 'Keno unavailable' : 'Connecting to Keno…'}</small>
    </header>

    <section className="ff-keno__board" aria-label="Keno ticket">
      <div className="ff-keno__controls">
        <p aria-live="polite">{selectedNumbers.length} of {maximumSelections} numbers selected{selectedNumbers.length === 0 ? ' — select at least one to enable draw.' : ''}</p>
        <button type="button" onClick={clearSelection} disabled={busy || recovery !== 'ready' || selectedNumbers.length === 0}>Clear selection</button>
      </div>
      <div className="ff-keno__grid" role="group" aria-label="Keno number selection">
        {kenoNumbers.map(number => {
          const isSelected = selectedNumbers.includes(number)
          const isDrawn = drawnNumbers?.has(number) ?? false
          const isHit = isDrawn && (ticketNumbers?.has(number) ?? false)
          const isMissed = round !== null && !isDrawn && (ticketNumbers?.has(number) ?? false)
          const classes = [
            isSelected ? 'is-selected' : '',
            isDrawn ? 'is-drawn' : '',
            isHit ? 'is-hit' : '',
            isMissed ? 'is-missed' : '',
          ].filter(Boolean).join(' ')
          const resultLabel = isHit ? ', hit' : isMissed ? ', missed' : isDrawn ? ', drawn' : ''
          return <button
            key={number}
            type="button"
            className={classes || undefined}
            aria-label={`Number ${number}${resultLabel}`}
            aria-pressed={isSelected}
            disabled={busy || recovery !== 'ready' || !status?.available || (!isSelected && isFull)}
            onClick={() => toggleNumber(number)}>{number}</button>
        })}
      </div>
      <button className="ff-keno__play" type="button" disabled={!canPlay} onClick={play}>{busy ? 'Playing Keno…' : 'Draw Keno'}</button>
      {recovery === 'recovering' && <p className="ff-keno__state" role="status">Restoring your pending Keno draw…</p>}
      {recovery === 'failed' && <button className="ff-keno__play" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
      {!status && error && !tableUnavailable && <button className="ff-keno__play" type="button" onClick={retryStatus}>Retry connection</button>}
      {round && <section className="ff-keno__result" aria-live="polite">
        <span>Round result</span>
        <strong>{round.hitCount} {round.hitCount === 1 ? 'hit' : 'hits'}</strong>
        <p className="ff-keno__result-summary"><b>{round.hitCount} of {round.ticket.numbers.length} picks hit.</b> Green numbers are your hits; amber numbers are drawn.</p>
        <div className="ff-keno__legend" aria-label="Keno result legend"><span className="is-hit">Hit</span><span className="is-drawn">Drawn</span><span className="is-missed">Missed pick</span></div>
        <p aria-label="Keno draw">Draw: {round.draw.numbers.join(', ')}</p>
      </section>}
      {error && <p className="ff-keno__error" role="alert">{error}</p>}
    </section>
  </main>
}

function messageForError(reason: unknown) {
  return reason instanceof KenoGatewayError ? reason.message : 'The Keno request could not be completed. Please try again.'
}
function isTableUnavailable(reason: unknown) { return reason instanceof KenoGatewayError && reason.code === 'keno-disabled' }
function createRequestKey() { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `keno-${random}` }
function pendingDrawKey(playerId: string) { return `fortuneforge:keno:pending:${playerId}` }
function readPendingDraw(playerId: string): StoredPendingDraw | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingDrawKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const draw = value as Record<string, unknown>
    if (typeof draw.idempotencyKey === 'string' && Array.isArray(draw.numbers) && draw.numbers.length > 0 && draw.numbers.length <= maximumSelections && draw.numbers.every(isKenoNumber) && new Set(draw.numbers).size === draw.numbers.length) {
      return { idempotencyKey: draw.idempotencyKey, numbers: [...draw.numbers].sort((left, right) => left - right) }
    }
  } catch { /* session storage is optional */ }
  return null
}
function isKenoNumber(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 80 }
function storePendingDraw(playerId: string | undefined, draw: StoredPendingDraw) { if (!playerId) return; try { sessionStorage.setItem(pendingDrawKey(playerId), JSON.stringify(draw)) } catch { /* session storage is optional */ } }
function clearPendingDraw(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingDrawKey(playerId)) } catch { /* session storage is optional */ } }

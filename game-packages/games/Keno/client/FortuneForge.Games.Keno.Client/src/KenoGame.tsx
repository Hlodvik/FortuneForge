import { useEffect, useMemo, useRef, useState } from 'react'
import { KenoGatewayError, type KenoGateway, type KenoRound, type KenoStatus } from './contracts'
import { HttpKenoGateway } from './httpKenoGateway'
import './keno.css'
import './kenoViewport.css'

export type KenoGameProps = Readonly<{ gateway?: KenoGateway; playerId?: string; initialSelection?: readonly number[]; onBalanceChange?: (balance: number) => void }>

const maximumSelections = 10
const kenoNumbers = Array.from({ length: 80 }, (_, index) => index + 1)
const quickPickCounts = [1, 3, 5, 7, 10] as const
const revealDelayMilliseconds = 70
const defaultGateway = new HttpKenoGateway()
type StoredPendingDraw = Readonly<{ idempotencyKey: string; numbers: readonly number[] }>

export function KenoGame({ gateway = defaultGateway, playerId, initialSelection = [], onBalanceChange }: KenoGameProps) {
  const [status, setStatus] = useState<KenoStatus | null>(null)
  const [selectedNumbers, setSelectedNumbers] = useState(() => canonicalTicket(initialSelection))
  const [round, setRound] = useState<KenoRound | null>(null)
  const [revealedCount, setRevealedCount] = useState(0)
  const [favoriteNumbers, setFavoriteNumbers] = useState<readonly number[]>(() => readFavoriteTicket(playerId))
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

  useEffect(() => setFavoriteNumbers(readFavoriteTicket(playerId)), [playerId])

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
      setSelectedNumbers([...nextRound.ticket.numbers])
      setRound(nextRound)
      setRevealedCount(0)
      onBalanceChange?.(nextRound.balance)
      setRecovery('ready')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      setRecovery('failed')
      setError('Your pending Keno draw could not be restored. Retry restoration before changing this ticket.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  useEffect(() => {
    if (round === null || revealedCount >= round.draw.numbers.length) return undefined
    const timer = window.setTimeout(() => setRevealedCount(count => count + 1), revealDelayMilliseconds)
    return () => window.clearTimeout(timer)
  }, [revealedCount, round])

  const isFull = selectedNumbers.length === maximumSelections
  const isRevealing = round !== null && revealedCount < round.draw.numbers.length
  const isLocked = busy || isRevealing || recovery !== 'ready'
  const canPlay = !isLocked && status?.available === true && selectedNumbers.length > 0
  const drawnNumbers = round ? new Set(round.draw.numbers.slice(0, revealedCount)) : null
  const ticketNumbers = round ? new Set(round.ticket.numbers) : null
  const oddsGuide = useMemo(() => createOddsGuide(selectedNumbers.length), [selectedNumbers.length])

  const resetRound = () => {
    roundRequestKey.current = null
    clearPendingDraw(playerId)
    setRound(null)
    setRevealedCount(0)
    setError(null)
  }

  const toggleNumber = (number: number) => {
    resetRound()
    setSelectedNumbers(current => current.includes(number)
      ? current.filter(selected => selected !== number)
      : current.length < maximumSelections ? [...current, number].sort((left, right) => left - right) : current)
  }

  const clearSelection = () => {
    resetRound()
    setSelectedNumbers([])
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  const quickPick = (count: number) => {
    if (isLocked) return
    resetRound()
    setSelectedNumbers(randomTicket(count))
  }

  const saveFavorite = () => {
    if (selectedNumbers.length === 0 || isLocked) return
    const numbers = [...selectedNumbers]
    setFavoriteNumbers(numbers)
    storeFavoriteTicket(playerId, numbers)
  }

  const loadFavorite = () => {
    if (favoriteNumbers.length === 0 || isLocked) return
    resetRound()
    setSelectedNumbers([...favoriteNumbers])
  }

  const startRound = (numbers: readonly number[]) => {
    if (isLocked || !status?.available) return
    if (numbers.length === 0) {
      setError('Select from 1 to 10 Keno numbers before playing.')
      return
    }

    setBusy(true)
    setError(null)
    setRound(null)
    setRevealedCount(0)
    const idempotencyKey = roundRequestKey.current ??= createRequestKey()
    storePendingDraw(playerId, { idempotencyKey, numbers })
    void gateway.createRound({ ticket: { numbers } }, { idempotencyKey })
      .then(nextRound => {
        roundRequestKey.current = null
        clearPendingDraw(playerId)
        setSelectedNumbers([...nextRound.ticket.numbers])
        setRound(nextRound)
        setRevealedCount(0)
        onBalanceChange?.(nextRound.balance)
      })
      .catch(reason => setError(messageForError(reason)))
      .finally(() => setBusy(false))
  }

  const repeatDraw = () => {
    if (round === null || isLocked) return
    const numbers = [...round.ticket.numbers]
    setSelectedNumbers(numbers)
    startRound(numbers)
  }

  return <main className="ff-keno" aria-busy={busy || isRevealing}>
    <header className="ff-keno__header">
      <span className="ff-keno__eyebrow">Choose your lucky numbers</span>
      <h1>Keno</h1>
      <p>Build a ticket, reveal 20 balls, and see how many of your picks hit.</p>
      <div className="ff-keno__status-row">
        <small>{status?.available ? 'Keno ready' : status ? 'Keno unavailable' : 'Connecting to Keno…'}</small>
        <span>Free play · no wager · no credit payout</span>
      </div>
    </header>

    <section className={`ff-keno__board${round ? ' ff-keno__board--has-round' : ''}`} aria-label="Keno ticket">
      <section className="ff-keno__ticket-tools" aria-label="Ticket tools">
        <div>
          <span className="ff-keno__label">Quick Pick</span>
          <div className="ff-keno__quick-picks">
            {quickPickCounts.map(count => <button key={count} type="button" disabled={isLocked || status?.available !== true} onClick={() => quickPick(count)} aria-label={`Quick pick ${count} ${count === 1 ? 'number' : 'numbers'}`}>{count}</button>)}
          </div>
        </div>
        <div className="ff-keno__ticket-actions">
          <button type="button" onClick={saveFavorite} disabled={isLocked || selectedNumbers.length === 0}>Save ticket</button>
          <button type="button" onClick={loadFavorite} disabled={isLocked || favoriteNumbers.length === 0}>Load saved</button>
        </div>
      </section>

      <div className="ff-keno__controls">
        <p aria-live="polite">{selectedNumbers.length} of {maximumSelections} numbers selected{selectedNumbers.length === 0 ? ' — select at least one to enable draw.' : ''}</p>
        <button type="button" onClick={clearSelection} disabled={isLocked || selectedNumbers.length === 0}>Clear selection</button>
      </div>
      {selectedNumbers.length > 0 && <div className="ff-keno__ticket-strip" aria-label="Current Keno ticket">{selectedNumbers.map(number => <span key={number}>{number}</span>)}</div>}

      <div className="ff-keno__grid" role="group" aria-label="Keno number selection">
        {kenoNumbers.map(number => {
          const isSelected = selectedNumbers.includes(number)
          const isDrawn = drawnNumbers?.has(number) ?? false
          const isHit = isDrawn && (ticketNumbers?.has(number) ?? false)
          const isMissed = round !== null && !isRevealing && !isDrawn && (ticketNumbers?.has(number) ?? false)
          const classes = [isSelected ? 'is-selected' : '', isDrawn ? 'is-drawn' : '', isHit ? 'is-hit' : '', isMissed ? 'is-missed' : ''].filter(Boolean).join(' ')
          const resultLabel = isHit ? ', hit' : isMissed ? ', missed' : isDrawn ? ', drawn' : ''
          return <button
            key={number}
            type="button"
            className={classes || undefined}
            aria-label={`Number ${number}${resultLabel}`}
            aria-pressed={isSelected}
            disabled={isLocked || !status?.available || (!isSelected && isFull)}
            onClick={() => toggleNumber(number)}>{number}</button>
        })}
      </div>

      <button className="ff-keno__play" type="button" disabled={!canPlay} onClick={() => startRound(selectedNumbers)}>{busy ? 'Starting draw…' : isRevealing ? `Revealing ${revealedCount} of 20…` : 'Draw Keno'}</button>
      {recovery === 'recovering' && <p className="ff-keno__state" role="status">Restoring your pending Keno draw…</p>}
      {recovery === 'failed' && <button className="ff-keno__play" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
      {!status && error && !tableUnavailable && <button className="ff-keno__play" type="button" onClick={retryStatus}>Retry connection</button>}

      {round && <section className="ff-keno__result" aria-live="polite">
        <span>{isRevealing ? 'Drawing live' : 'Round result'}</span>
        <strong>{isRevealing ? `${revealedCount} / 20 balls` : `${round.hitCount} ${round.hitCount === 1 ? 'hit' : 'hits'}`}</strong>
        <div className="ff-keno__draw-tray" aria-label="Revealed Keno balls">{round.draw.numbers.slice(0, revealedCount).map(number => <span key={number} className={ticketNumbers?.has(number) ? 'is-hit' : undefined}>{number}</span>)}</div>
        {!isRevealing && <p className="ff-keno__result-summary"><b>{round.hitCount} of {round.ticket.numbers.length} picks hit.</b> Green numbers are your hits; amber numbers are drawn.</p>}
        <div className="ff-keno__legend" aria-label="Keno result legend"><span className="is-hit">Hit</span><span className="is-drawn">Drawn</span><span className="is-missed">Missed pick</span></div>
        {!isRevealing && <p aria-label="Keno draw">Draw: {round.draw.numbers.join(', ')}</p>}
        {!isRevealing && <button className="ff-keno__repeat" type="button" onClick={repeatDraw}>Repeat this ticket</button>}
      </section>}
      {error && <p className="ff-keno__error" role="alert">{error}</p>}

      <section className="ff-keno__odds" aria-label="Keno odds guide">
        <div>
          <span className="ff-keno__label">Hit odds</span>
          <h2>{selectedNumbers.length === 0 ? 'Choose a ticket size' : `${selectedNumbers.length}-spot ticket`}</h2>
          <p>This table is free play and awards no credits. Odds update with your ticket size before every draw.</p>
        </div>
        {oddsGuide.length > 0 && <div className="ff-keno__odds-grid" role="table" aria-label={`${selectedNumbers.length}-spot hit probabilities`}>
          {oddsGuide.map(row => <div key={row.hits} role="row"><span role="cell">{row.hits} {row.hits === 1 ? 'hit' : 'hits'}</span><strong role="cell">{formatProbability(row.probability)}</strong></div>)}
        </div>}
      </section>
    </section>
  </main>
}

function messageForError(reason: unknown) {
  return reason instanceof KenoGatewayError ? reason.message : 'The Keno request could not be completed. Please try again.'
}
function isTableUnavailable(reason: unknown) { return reason instanceof KenoGatewayError && reason.code === 'keno-disabled' }
function createRequestKey() { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `keno-${random}` }
function pendingDrawKey(playerId: string) { return `fortuneforge:keno:pending:${playerId}` }
function favoriteTicketKey(playerId: string | undefined) { return `fortuneforge:keno:favorite:${playerId ?? 'local'}` }

function canonicalTicket(numbers: readonly number[]) {
  return [...new Set(numbers.filter(isKenoNumber))].slice(0, maximumSelections).sort((left, right) => left - right)
}

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

function readFavoriteTicket(playerId: string | undefined): readonly number[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(favoriteTicketKey(playerId)) ?? 'null')
    if (Array.isArray(value) && value.length > 0 && value.length <= maximumSelections && value.every(isKenoNumber) && new Set(value).size === value.length)
      return [...value].sort((left, right) => left - right)
  } catch { /* local storage is optional */ }
  return []
}

function storeFavoriteTicket(playerId: string | undefined, numbers: readonly number[]) {
  try { localStorage.setItem(favoriteTicketKey(playerId), JSON.stringify(numbers)) } catch { /* local storage is optional */ }
}

function randomTicket(count: number): number[] {
  const pool = [...kenoNumbers]
  for (let index = pool.length - 1; index > 0; index--) {
    const target = randomIndex(index + 1)
    ;[pool[index], pool[target]] = [pool[target]!, pool[index]!]
  }
  return pool.slice(0, Math.min(maximumSelections, Math.max(1, count))).sort((left, right) => left - right)
}

function randomIndex(length: number): number {
  if (typeof crypto?.getRandomValues === 'function') {
    const sample = new Uint32Array(1)
    crypto.getRandomValues(sample)
    return sample[0]! % length
  }
  return Math.floor(Math.random() * length)
}

function createOddsGuide(pickCount: number): ReadonlyArray<Readonly<{ hits: number; probability: number }>> {
  if (pickCount < 1 || pickCount > maximumSelections) return []
  return Array.from({ length: pickCount + 1 }, (_, hits) => ({
    hits,
    probability: (combination(pickCount, hits) * combination(80 - pickCount, 20 - hits)) / combination(80, 20),
  })).reverse()
}

function combination(total: number, selected: number): number {
  if (selected < 0 || selected > total) return 0
  const count = Math.min(selected, total - selected)
  let result = 1
  for (let index = 1; index <= count; index++) result = (result * (total - count + index)) / index
  return result
}

function formatProbability(probability: number): string {
  if (probability <= 0) return 'Impossible'
  if (probability >= 0.01) return `${(probability * 100).toFixed(probability >= 0.1 ? 1 : 2)}%`
  return `1 in ${Math.round(1 / probability).toLocaleString('en-US')}`
}

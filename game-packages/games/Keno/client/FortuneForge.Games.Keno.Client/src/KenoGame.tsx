import { useEffect, useMemo, useRef, useState } from 'react'
import { KenoGatewayError, type KenoGateway, type KenoRound, type KenoStatus } from './contracts'
import { HttpKenoGateway } from './httpKenoGateway'
import drawTumbleUrl from './assets/keno-draw-tumble.wav?url'
import './keno.css'
import './kenoViewport.css'

export type KenoGameProps = Readonly<{ gateway?: KenoGateway; playerId?: string; initialSelection?: readonly number[]; onBalanceChange?: (balance: number) => void }>

const maximumSelections = 10
const kenoNumbers = Array.from({ length: 80 }, (_, index) => index + 1)
const quickPickCounts = [1, 3, 5, 7, 10] as const
const revealBeatMilliseconds = 110
const firstRevealBeatMilliseconds = 55
const finishCueDelaySeconds = .14
const defaultGateway = new HttpKenoGateway()
type StoredPendingDraw = Readonly<{ idempotencyKey: string; numbers: readonly number[]; wager: number }>

export function KenoGame({ gateway = defaultGateway, playerId, initialSelection = [], onBalanceChange }: KenoGameProps) {
  const [status, setStatus] = useState<KenoStatus | null>(null)
  const [selectedNumbers, setSelectedNumbers] = useState(() => canonicalTicket(initialSelection))
  const [wager, setWager] = useState(1)
  const [round, setRound] = useState<KenoRound | null>(null)
  const [revealedCount, setRevealedCount] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const roundRequestKey = useRef<string | null>(null)
  const pickAudioContext = useRef<AudioContext | null>(null)
  const drawAudioBuffer = useRef<AudioBuffer | null>(null)
  const drawAudioLoad = useRef<Promise<AudioBuffer | null> | null>(null)
  const drawAudioSource = useRef<AudioBufferSourceNode | null>(null)
  const revealStartedAt = useRef<number | null>(null)
  const completedAudioRound = useRef<string | null>(null)

  useEffect(() => () => {
    stopDrawAudio(drawAudioSource)
    const context = pickAudioContext.current
    pickAudioContext.current = null
    if (context && context.state !== 'closed') void context.close()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setWager(current => current >= nextStatus.minimumWager && current <= nextStatus.maximumWager ? current : nextStatus.minimumWager)
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
    void gateway.createRound({ ticket: { numbers: pendingDraw.numbers }, wager: pendingDraw.wager }, {
      signal: controller.signal,
      idempotencyKey: pendingDraw.idempotencyKey,
    }).then(nextRound => {
      clearPendingDraw(playerId)
      setSelectedNumbers([...nextRound.ticket.numbers])
      setWager(nextRound.wager)
      revealStartedAt.current = nowMilliseconds()
      completedAudioRound.current = null
      setRound(nextRound)
      setRevealedCount(0)
      setStatus(current => current ? { ...current, balance: nextRound.balance } : current)
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
    if (round === null) return undefined
    if (revealedCount >= round.draw.numbers.length) {
      if (completedAudioRound.current !== round.roundId) {
        completedAudioRound.current = round.roundId
        playRoundCompleteEffect(pickAudioContext, round.payout > 0, finishCueDelaySeconds)
      }
      return undefined
    }

    const startedAt = revealStartedAt.current ??= nowMilliseconds()
    const targetBeat = startedAt + firstRevealBeatMilliseconds + (revealedCount * revealBeatMilliseconds)
    const delay = Math.max(0, targetBeat - nowMilliseconds())
    const timer = window.setTimeout(() => setRevealedCount(count => Math.min(count + 1, round.draw.numbers.length)), delay)
    return () => window.clearTimeout(timer)
  }, [revealedCount, round])

  const isFull = selectedNumbers.length === maximumSelections
  const isRevealing = round !== null && revealedCount < round.draw.numbers.length
  const isLocked = busy || isRevealing || recovery !== 'ready'
  const canPlay = !isLocked && status?.available === true && selectedNumbers.length > 0 && status.balance >= wager
  const drawnNumbers = round ? new Set(round.draw.numbers.slice(0, revealedCount)) : null
  const ticketNumbers = round ? new Set(round.ticket.numbers) : null
  const prizeTiers = useMemo(() => status?.paytable
    .filter(tier => tier.spots === selectedNumbers.length)
    .sort((left, right) => right.hits - left.hits) ?? [], [selectedNumbers.length, status])
  const wagerOptions = useMemo(() => [1, 2, 5, 10, 20]
    .filter(value => status && value >= status.minimumWager && value <= status.maximumWager), [status])

  const resetRound = () => {
    roundRequestKey.current = null
    stopDrawAudio(drawAudioSource)
    revealStartedAt.current = null
    completedAudioRound.current = null
    clearPendingDraw(playerId)
    setRound(null)
    setRevealedCount(0)
    setError(null)
  }

  const toggleNumber = (number: number) => {
    playPickEffect(pickAudioContext, selectedNumbers.includes(number) ? 'remove' : 'add')
    resetRound()
    setSelectedNumbers(current => current.includes(number)
      ? current.filter(selected => selected !== number)
      : current.length < maximumSelections ? [...current, number].sort((left, right) => left - right) : current)
  }

  const clearSelection = () => {
    playPickEffect(pickAudioContext, 'remove')
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
    playPickEffect(pickAudioContext, 'add')
    resetRound()
    setSelectedNumbers(randomTicket(count))
  }

  const chooseWager = (value: number) => {
    if (isLocked || !status || value < status.minimumWager || value > status.maximumWager) return
    resetRound()
    setWager(value)
  }

  const startRound = (numbers: readonly number[], stake = wager) => {
    if (isLocked || !status?.available) return
    if (numbers.length === 0) {
      setError('Select from 1 to 10 Keno numbers before playing.')
      return
    }

    setBusy(true)
    setError(null)
    stopDrawAudio(drawAudioSource)
    setRound(null)
    setRevealedCount(0)
    completedAudioRound.current = null
    void prepareDrawAudio(pickAudioContext, drawAudioBuffer, drawAudioLoad)
    const idempotencyKey = roundRequestKey.current ??= createRequestKey()
    storePendingDraw(playerId, { idempotencyKey, numbers, wager: stake })
    void gateway.createRound({ ticket: { numbers }, wager: stake }, { idempotencyKey })
      .then(async nextRound => {
        roundRequestKey.current = null
        clearPendingDraw(playerId)
        setSelectedNumbers([...nextRound.ticket.numbers])
        setWager(nextRound.wager)
        revealStartedAt.current = await playDrawAudio(pickAudioContext, drawAudioBuffer, drawAudioLoad, drawAudioSource)
        setRound(nextRound)
        setRevealedCount(0)
        setStatus(current => current ? { ...current, balance: nextRound.balance } : current)
        onBalanceChange?.(nextRound.balance)
      })
      .catch(reason => setError(messageForError(reason)))
      .finally(() => setBusy(false))
  }

  return <main className="ff-keno" aria-busy={busy || isRevealing}>
    <header className="ff-keno__header">
      <span className="ff-keno__eyebrow">Choose your lucky numbers</span>
      <h1>Keno</h1>
      <p>Build a ticket, reveal 20 balls, and see how many of your picks hit.</p>
    </header>

    <section className={`ff-keno__board${round ? ' ff-keno__board--has-round' : ''}`} aria-label="Keno ticket">
      <section className="ff-keno__ticket-tools" aria-label="Ticket tools">
        <div>
          <span className="ff-keno__label">Quick Pick</span>
          <div className="ff-keno__quick-picks">
            {quickPickCounts.map(count => <button key={count} type="button" disabled={isLocked || status?.available !== true} onClick={() => quickPick(count)} aria-label={`Quick pick ${count} ${count === 1 ? 'number' : 'numbers'}`}>{count}</button>)}
          </div>
        </div>
        <label className="ff-keno__wager">Wager
          <select aria-label="Keno wager" value={wager} disabled={isLocked || !status?.available} onChange={event => chooseWager(Number(event.target.value))}>
            {wagerOptions.map(value => <option key={value} value={value}>{formatMoney(value)}</option>)}
          </select>
          <small>Balance {formatMoney(status?.balance ?? 0)}</small>
        </label>
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

      <button className="ff-keno__play" type="button" disabled={!canPlay} onClick={() => startRound(selectedNumbers)}>{busy ? 'Starting draw…' : isRevealing ? `Revealing ${revealedCount} of 20…` : 'Draw'}</button>
      {recovery === 'recovering' && <p className="ff-keno__state" role="status">Restoring your pending Keno draw…</p>}
      {recovery === 'failed' && <button className="ff-keno__play" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
      {!status && error && !tableUnavailable && <button className="ff-keno__play" type="button" onClick={retryStatus}>Retry connection</button>}

      {round && <section className="ff-keno__result" aria-live="polite">
        <span>{isRevealing ? 'Drawing live' : 'Round result'}</span>
        <strong className={!isRevealing ? round.payout > 0 ? 'is-win' : 'is-loss' : undefined}>{isRevealing ? `${revealedCount} / 20 balls` : round.payout > 0 ? `${formatMoney(round.payout)} WIN` : 'No win'}</strong>
        <div className="ff-keno__draw-tray" aria-label="Revealed Keno balls">{round.draw.numbers.slice(0, revealedCount).map(number => <span key={number} className={ticketNumbers?.has(number) ? 'is-hit' : undefined}>{number}</span>)}</div>
        {!isRevealing && <p className="ff-keno__result-summary"><b>{round.hitCount} of {round.ticket.numbers.length} picks hit.</b></p>}
        {!isRevealing && <p aria-label="Keno draw">Draw: {round.draw.numbers.join(', ')}</p>}
        {!isRevealing && <p className="ff-keno__settlement">Wager {formatMoney(round.wager)} · Balance {formatMoney(round.balance)}</p>}
      </section>}
      {error && <p className="ff-keno__error" role="alert">{error}</p>}

      <section className="ff-keno__odds" aria-label="Keno prize table">
        <div>
          <span className="ff-keno__label">Prize table</span>
          <h2>{selectedNumbers.length === 0 ? 'Choose a ticket size' : `${selectedNumbers.length}-spot payouts`}</h2>
          <p>{formatMoney(wager)} ticket · balance {formatMoney(status?.balance ?? 0)}</p>
        </div>
        {prizeTiers.length > 0 && <div className="ff-keno__odds-grid" role="table" aria-label={`${selectedNumbers.length}-spot Keno payouts`}>
          {prizeTiers.map(tier => <div key={tier.hits} role="row"><span role="cell">Match {tier.hits}</span><strong role="cell">{formatMoney(wager * tier.multiplier)}</strong></div>)}
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

function canonicalTicket(numbers: readonly number[]) {
  return [...new Set(numbers.filter(isKenoNumber))].slice(0, maximumSelections).sort((left, right) => left - right)
}

function readPendingDraw(playerId: string): StoredPendingDraw | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingDrawKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const draw = value as Record<string, unknown>
    if (typeof draw.idempotencyKey === 'string' && typeof draw.wager === 'number' && Number.isFinite(draw.wager) && draw.wager > 0 && Array.isArray(draw.numbers) && draw.numbers.length > 0 && draw.numbers.length <= maximumSelections && draw.numbers.every(isKenoNumber) && new Set(draw.numbers).size === draw.numbers.length) {
      return { idempotencyKey: draw.idempotencyKey, numbers: [...draw.numbers].sort((left, right) => left - right), wager: draw.wager }
    }
  } catch { /* session storage is optional */ }
  return null
}

function isKenoNumber(value: unknown): value is number { return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 80 }
function storePendingDraw(playerId: string | undefined, draw: StoredPendingDraw) { if (!playerId) return; try { sessionStorage.setItem(pendingDrawKey(playerId), JSON.stringify(draw)) } catch { /* session storage is optional */ } }
function clearPendingDraw(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingDrawKey(playerId)) } catch { /* session storage is optional */ } }

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

function formatMoney(value: number): string { return `R${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }

function playPickEffect(contextRef: { current: AudioContext | null }, action: 'add' | 'remove'): void {
  const context = getKenoAudioContext(contextRef)
  if (!context) return
  if (context.state === 'suspended') void context.resume().catch(() => undefined)

  const now = context.currentTime
  const duration = action === 'add' ? .095 : .075
  const oscillator = context.createOscillator()
  const gain = context.createGain()
  oscillator.type = 'sine'
  oscillator.frequency.setValueAtTime(action === 'add' ? 155 : 120, now)
  oscillator.frequency.exponentialRampToValueAtTime(action === 'add' ? 82 : 68, now + duration)
  gain.gain.setValueAtTime(.0001, now)
  gain.gain.exponentialRampToValueAtTime(action === 'add' ? .032 : .024, now + .007)
  gain.gain.exponentialRampToValueAtTime(.0001, now + duration)
  oscillator.connect(gain).connect(context.destination)
  oscillator.start(now)
  oscillator.stop(now + duration + .01)
}

function getKenoAudioContext(contextRef: { current: AudioContext | null }): AudioContext | null {
  if (typeof window === 'undefined') return null
  const AudioContextConstructor = window.AudioContext
    ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
  if (!AudioContextConstructor) return null
  return contextRef.current ??= new AudioContextConstructor()
}

function prepareDrawAudio(
  contextRef: { current: AudioContext | null },
  bufferRef: { current: AudioBuffer | null },
  loadRef: { current: Promise<AudioBuffer | null> | null },
): Promise<AudioBuffer | null> {
  if (bufferRef.current) return Promise.resolve(bufferRef.current)
  if (loadRef.current) return loadRef.current
  const context = getKenoAudioContext(contextRef)
  if (!context) return Promise.resolve(null)
  if (context.state === 'suspended') void context.resume().catch(() => undefined)

  const request = fetch(drawTumbleUrl)
    .then(response => {
      if (!response.ok) throw new Error('Keno draw audio could not be loaded.')
      return response.arrayBuffer()
    })
    .then(data => context.decodeAudioData(data.slice(0)))
    .then(buffer => {
      bufferRef.current = buffer
      return buffer
    })
    .catch(() => {
      if (loadRef.current === request) loadRef.current = null
      return null
    })
  loadRef.current = request
  return request
}

async function playDrawAudio(
  contextRef: { current: AudioContext | null },
  bufferRef: { current: AudioBuffer | null },
  loadRef: { current: Promise<AudioBuffer | null> | null },
  sourceRef: { current: AudioBufferSourceNode | null },
): Promise<number> {
  const context = getKenoAudioContext(contextRef)
  if (!context) return nowMilliseconds()
  if (context.state === 'suspended') await context.resume().catch(() => undefined)
  const buffer = bufferRef.current ?? await prepareDrawAudio(contextRef, bufferRef, loadRef)
  if (!buffer) return nowMilliseconds()

  stopDrawAudio(sourceRef)
  const source = context.createBufferSource()
  source.buffer = buffer
  source.connect(context.destination)
  sourceRef.current = source
  const startedAt = nowMilliseconds()
  source.start(context.currentTime)
  return startedAt
}

function stopDrawAudio(sourceRef: { current: AudioBufferSourceNode | null }): void {
  const source = sourceRef.current
  sourceRef.current = null
  if (!source) return
  try { source.stop() } catch { /* an ended one-shot source is already stopped */ }
}

function playRoundCompleteEffect(contextRef: { current: AudioContext | null }, won: boolean, delay: number): void {
  const context = getKenoAudioContext(contextRef)
  if (!context) return
  if (context.state === 'suspended') void context.resume().catch(() => undefined)
  const start = context.currentTime + delay
  if (won) {
    playKenoTone(context, 480, 650, start, .13, .038, 'triangle')
    playKenoTone(context, 690, 940, start + .065, .16, .032, 'sine')
  } else {
    playKenoTone(context, 230, 145, start, .12, .026, 'sine')
  }
}

function playKenoTone(context: AudioContext, startFrequency: number, endFrequency: number, start: number, duration: number, volume: number, type: OscillatorType): void {
  const oscillator = context.createOscillator()
  const gain = context.createGain()
  oscillator.type = type
  oscillator.frequency.setValueAtTime(startFrequency, start)
  oscillator.frequency.exponentialRampToValueAtTime(endFrequency, start + duration)
  gain.gain.setValueAtTime(.0001, start)
  gain.gain.exponentialRampToValueAtTime(volume, start + .008)
  gain.gain.exponentialRampToValueAtTime(.0001, start + duration)
  oscillator.connect(gain).connect(context.destination)
  oscillator.start(start)
  oscillator.stop(start + duration + .01)
}

function nowMilliseconds(): number { return typeof performance === 'undefined' ? Date.now() : performance.now() }

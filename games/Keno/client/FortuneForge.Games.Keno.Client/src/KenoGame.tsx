import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { KenoGatewayError, type KenoGateway, type KenoRound, type KenoStatus } from './contracts'
import { HttpKenoGateway } from './httpKenoGateway'
import drawTumbleUrl from './assets/keno-draw-tumble.wav?url'
import './keno.css'
import './kenoViewport.css'

export type KenoGameProps = Readonly<{ gateway?: KenoGateway; playerId?: string; initialSelection?: readonly number[]; onBalanceChange?: (balance: number) => void }>

const maximumSelections = 10
const kenoNumbers = Array.from({ length: 80 }, (_, index) => index + 1)
const revealBeatMilliseconds = 110
const firstRevealBeatMilliseconds = 55
const finishCueDelaySeconds = .14
const boardClearDelayMilliseconds = 900
const boardClearDurationMilliseconds = 550
const defaultGateway = new HttpKenoGateway()
type StoredPendingDraw = Readonly<{ idempotencyKey: string; numbers: readonly number[]; wager: number }>

export function KenoGame({ gateway = defaultGateway, playerId, initialSelection = [], onBalanceChange }: KenoGameProps) {
  const wagerId = useId()
  const [status, setStatus] = useState<KenoStatus | null>(null)
  const [selectedNumbers, setSelectedNumbers] = useState(() => canonicalTicket(initialSelection))
  const [wager, setWager] = useState(1)
  const [round, setRound] = useState<KenoRound | null>(null)
  const [revealedCount, setRevealedCount] = useState(0)
  const [boardResetting, setBoardResetting] = useState(false)
  const [boardCleared, setBoardCleared] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [focusedNumber, setFocusedNumber] = useState(1)
  const [muted, setMuted] = useState(readMuted)
  const mutedRef = useRef(muted)
  mutedRef.current = muted
  const roundRequestKey = useRef<string | null>(null)
  const pickAudioContext = useRef<AudioContext | null>(null)
  const drawAudioBuffer = useRef<AudioBuffer | null>(null)
  const drawAudioLoad = useRef<Promise<AudioBuffer | null> | null>(null)
  const drawAudioSource = useRef<AudioBufferSourceNode | null>(null)
  const revealStartedAt = useRef<number | null>(null)
  const completedAudioRound = useRef<string | null>(null)
  const activeTones = useRef(new Set<OscillatorNode>())

  useEffect(() => {
    if (muted) {
      stopDrawAudio(drawAudioSource)
      stopTones(activeTones.current)
    }
    try { localStorage.setItem('fortuneforge:keno:muted', String(muted)) } catch { /* storage is optional */ }
  }, [muted])

  useEffect(() => () => {
    stopDrawAudio(drawAudioSource)
    stopTones(activeTones.current)
    const context = pickAudioContext.current
    pickAudioContext.current = null
    if (context && context.state !== 'closed') void context.close()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setWager(current => isSupportedWager(nextStatus, current) ? current : nextStatus.minimumWager)
      setTableUnavailable(!nextStatus.available)
      setStatusError(nextStatus.available ? null : 'This table is temporarily unavailable. Please choose another game.')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      const unavailable = isTableUnavailable(reason)
      setTableUnavailable(unavailable)
      setStatusError(unavailable ? 'This table is temporarily unavailable. Please choose another game.' : messageForError(reason))
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
      setBoardResetting(false)
      setBoardCleared(false)
      setRound(nextRound)
      setRevealedCount(0)
      setStatus(current => current ? { ...current, balance: nextRound.balance } : current)
      onBalanceChange?.(nextRound.balance)
      setRecovery('ready')
      setError(null)
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
        if (!mutedRef.current) playRoundCompleteEffect(pickAudioContext, round.net > 0, finishCueDelaySeconds, activeTones.current)
      }
      const beginReset = window.setTimeout(() => setBoardResetting(true), boardClearDelayMilliseconds)
      const finishReset = window.setTimeout(() => {
        setBoardResetting(false)
        setBoardCleared(true)
      }, boardClearDelayMilliseconds + boardClearDurationMilliseconds)
      return () => {
        window.clearTimeout(beginReset)
        window.clearTimeout(finishReset)
      }
    }

    const startedAt = revealStartedAt.current ??= nowMilliseconds()
    const targetBeat = startedAt + firstRevealBeatMilliseconds + (revealedCount * revealBeatMilliseconds)
    const delay = Math.max(0, targetBeat - nowMilliseconds())
    const timer = window.setTimeout(() => setRevealedCount(count => Math.min(count + 1, round.draw.numbers.length)), delay)
    return () => window.clearTimeout(timer)
  }, [revealedCount, round])

  const isFull = selectedNumbers.length === maximumSelections
  const isRevealing = round !== null && revealedCount < round.draw.numbers.length
  const isSettling = round !== null && !isRevealing && !boardCleared
  const isLocked = busy || isRevealing || isSettling || recovery !== 'ready'
  const canPlay = !isLocked && status?.available === true && selectedNumbers.length > 0 && status.balance >= wager
  const drawnNumbers = round && !boardCleared ? new Set(round.draw.numbers.slice(0, revealedCount)) : null
  const ticketNumbers = round ? new Set(round.ticket.numbers) : null
  const prizeTiers = useMemo(() => status?.paytable
    .filter(tier => tier.spots === selectedNumbers.length)
    .sort((left, right) => right.hits - left.hits) ?? [], [selectedNumbers.length, status])
  const wagerOptions = useMemo(() => status ? supportedWagers(status, wager) : round ? [round.wager] : [], [status, wager, round?.wager])
  const liveHits = round?.ticket.numbers.filter(number => round.draw.numbers.slice(0, revealedCount).includes(number)).length ?? 0
  const displayedBalance = status?.balance ?? round?.balance ?? null
  const visibleError = error ?? statusError
  const connectionFailed = round === null && visibleError !== null && (!status || !status.available)

  const resetRound = () => {
    roundRequestKey.current = null
    stopDrawAudio(drawAudioSource)
    revealStartedAt.current = null
    completedAudioRound.current = null
    setBoardResetting(false)
    setBoardCleared(false)
    clearPendingDraw(playerId)
    setRound(null)
    setRevealedCount(0)
    setError(null)
  }

  const toggleNumber = (number: number) => {
    if (!mutedRef.current) playPickEffect(pickAudioContext, selectedNumbers.includes(number) ? 'remove' : 'add', activeTones.current)
    resetRound()
    setSelectedNumbers(current => current.includes(number)
      ? current.filter(selected => selected !== number)
      : current.length < maximumSelections ? [...current, number].sort((left, right) => left - right) : current)
  }

  const clearSelection = () => {
    if (!mutedRef.current) playPickEffect(pickAudioContext, 'remove', activeTones.current)
    resetRound()
    setSelectedNumbers([])
  }

  const retryStatus = () => {
    setStatusError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  const chooseWager = (value: number) => {
    if (isLocked || !status || !isSupportedWager(status, value)) return
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
    setBoardResetting(false)
    setBoardCleared(false)
    completedAudioRound.current = null
    if (!mutedRef.current) void prepareDrawAudio(pickAudioContext, drawAudioBuffer, drawAudioLoad)
    const idempotencyKey = roundRequestKey.current ??= createRequestKey()
    storePendingDraw(playerId, { idempotencyKey, numbers, wager: stake })
    void gateway.createRound({ ticket: { numbers }, wager: stake }, { idempotencyKey })
      .then(async nextRound => {
        roundRequestKey.current = null
        clearPendingDraw(playerId)
        setSelectedNumbers([...nextRound.ticket.numbers])
        setWager(nextRound.wager)
        revealStartedAt.current = await playDrawAudio(pickAudioContext, drawAudioBuffer, drawAudioLoad, drawAudioSource, mutedRef)
        setBoardResetting(false)
        setBoardCleared(false)
        setRound(nextRound)
        setRevealedCount(0)
        setStatus(current => current ? { ...current, balance: nextRound.balance } : current)
        onBalanceChange?.(nextRound.balance)
      })
      .catch(reason => setError(messageForError(reason)))
      .finally(() => setBusy(false))
  }

  return <main className="ff-keno" aria-busy={busy || isRevealing} data-connection-failed={connectionFailed}>
    <header className="ff-keno__header">
      <span className="ff-keno__eyebrow">Choose your lucky numbers</span>
      <h1>Keno</h1>
      <p>Build a ticket, reveal 20 balls, and see how many of your picks hit.</p>
    </header>

    {connectionFailed ? <section className="ff-keno__board ff-keno__connection" aria-label="Keno connection">
      <p className="ff-keno__error" role="alert">{visibleError}</p>
      {recovery === 'failed' && <button className="ff-keno__play" type="button" onClick={() => { setError(null); setRecoveryAttempt(value => value + 1) }}>Retry restoration</button>}
      {recovery === 'recovering' && <p className="ff-keno__state" role="status">Restoring your pending Keno draw…</p>}
      {!tableUnavailable && <button className="ff-keno__play" type="button" onClick={retryStatus}>Retry connection</button>}
    </section> : <section className={`ff-keno__board${round ? ' ff-keno__board--has-round' : ''}`} aria-label="Keno ticket">
      <div className="ff-keno__controls">
        <p aria-live="polite">{selectedNumbers.length} of {maximumSelections} numbers selected{selectedNumbers.length === 0 ? ' — select at least one to enable draw.' : ''}</p>
        <button className="ff-keno__sound" type="button" aria-label="Mute Keno sound" aria-pressed={muted} onClick={() => {
          mutedRef.current = !muted
          setMuted(!muted)
        }}>
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
            <path d="M11 5 6 9H3v6h3l5 4Z" />
            {muted ? <path d="m16 9 6 6m0-6-6 6" /> : <><path d="M15 8a6 6 0 0 1 0 8" /><path d="M18 5a10 10 0 0 1 0 14" /></>}
          </svg>
        </button>
        <button type="button" onClick={clearSelection} disabled={isLocked || selectedNumbers.length === 0}>Clear selection</button>
      </div>

      <div className={`ff-keno__grid${round && !boardCleared ? ' is-drawing' : ''}${boardResetting ? ' is-resetting' : ''}${boardCleared ? ' is-ready' : ''}`} role="group" aria-label="Keno number selection">
        {kenoNumbers.map(number => {
          const isSelected = selectedNumbers.includes(number)
          const isDrawn = drawnNumbers?.has(number) ?? false
          const isHit = isDrawn && (ticketNumbers?.has(number) ?? false)
          const isMissed = round !== null && !isRevealing && !boardCleared && !isDrawn && (ticketNumbers?.has(number) ?? false)
          const classes = [isSelected ? 'is-selected' : '', isDrawn ? 'is-drawn' : '', isHit ? 'is-hit' : '', isMissed ? 'is-missed' : ''].filter(Boolean).join(' ')
          const resultLabel = isHit ? ', hit' : isMissed ? ', missed' : isDrawn ? ', drawn' : ''
          return <button
            key={number}
            type="button"
            className={classes || undefined}
            aria-label={`Number ${number}${resultLabel}`}
            aria-pressed={isSelected}
            tabIndex={number === focusedNumber || (isFull && !selectedNumbers.includes(focusedNumber) && number === selectedNumbers[0]) ? 0 : -1}
            disabled={isLocked || !status?.available || (!isSelected && isFull)}
            onFocus={() => setFocusedNumber(number)}
            onKeyDown={event => {
              const columns = getComputedStyle(event.currentTarget.parentElement!).gridTemplateColumns.split(' ').length
              const offset = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowDown' ? columns : event.key === 'ArrowUp' ? -columns : 0
              if (!offset && event.key !== 'Home' && event.key !== 'End') return
              event.preventDefault()
              const buttons = Array.from(event.currentTarget.parentElement!.querySelectorAll('button'))
              let next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (number - 1 + offset + buttons.length) % buttons.length
              for (let attempts = 0; attempts < buttons.length; attempts++) {
                if (!buttons[next].disabled) { buttons[next].focus(); break }
                next = (next + (offset < 0 || event.key === 'End' ? -1 : 1) + buttons.length) % buttons.length
              }
            }}
            onClick={() => toggleNumber(number)}>{number}</button>
        })}
      </div>

      <section className="ff-keno__draw-controls" aria-label="Keno draw controls">
        <div className="ff-keno__wager"><label htmlFor={wagerId}>Wager per draw</label>
          <span className="ff-keno__wager-adjust">
          <button type="button" aria-label="Decrease Keno wager" disabled={isLocked || !status?.available || wager <= status.minimumWager} onClick={() => status && chooseWager(roundMoney(Math.max(status.minimumWager, wager - status.wagerIncrement)))}>−</button>
          <select id={wagerId} aria-label="Keno wager" value={wager} disabled={isLocked || !status?.available} onChange={event => chooseWager(Number(event.target.value))}>
            {wagerOptions.map(value => <option key={value} value={value}>{formatMoney(value)}</option>)}
          </select>
          <button type="button" aria-label="Increase Keno wager" disabled={isLocked || !status?.available || wager + status.wagerIncrement > status.maximumWager} onClick={() => status && chooseWager(roundMoney(wager + status.wagerIncrement))}>+</button>
          </span>
          <small>Balance {displayedBalance === null ? '—' : formatMoney(displayedBalance)}</small>
        </div>
        {recovery === 'failed' ? <button className="ff-keno__play" type="button" onClick={() => { setError(null); setRecoveryAttempt(value => value + 1) }}>Retry restoration</button>
          : recovery === 'recovering' ? <button className="ff-keno__play" type="button" disabled>Restoring draw…</button>
          : !status && statusError && !tableUnavailable ? <button className="ff-keno__play" type="button" onClick={retryStatus}>Retry connection</button>
          : <button className="ff-keno__play" type="button" disabled={!canPlay} onClick={() => startRound(selectedNumbers)}>{busy ? 'Starting draw…' : isRevealing ? `Revealing ${revealedCount} of 20…` : round ? 'Draw again' : 'Draw'}</button>}
      </section>

      <aside className="ff-keno__feedback" aria-label="Keno prizes and result">
      {round && <section className="ff-keno__result" aria-live="polite">
        <span>{isRevealing ? 'Drawing live' : 'Round result'}</span>
        <strong className={!isRevealing ? round.net > 0 ? 'is-win' : round.payout === round.wager ? 'is-return' : 'is-loss' : undefined}>{isRevealing ? `${revealedCount} / 20 balls` : round.net > 0 ? `${formatMoney(round.payout)} WIN` : round.payout === round.wager ? `${formatMoney(round.payout)} RETURNED` : 'No win'}</strong>
        {isRevealing && <p className="ff-keno__result-summary"><b>{liveHits} of {round.ticket.numbers.length} picks hit.</b></p>}
        {!isRevealing && <p className="ff-keno__result-summary"><b>{round.hitCount} of {round.ticket.numbers.length} picks hit.</b></p>}
        {!isRevealing && <p className="ff-keno__settlement">Wager {formatMoney(round.wager)} · Balance {formatMoney(round.balance)}</p>}
      </section>}
      {visibleError && <p className="ff-keno__error" role="alert">{visibleError}</p>}

      {status && <section className="ff-keno__odds" aria-label="Keno prize table">
        <div>
          <span className="ff-keno__label">Prize table</span>
          {selectedNumbers.length > 0 && <h2>{selectedNumbers.length}-spot payouts</h2>}
          <p>{formatMoney(wager)} ticket · balance {formatMoney(status?.balance ?? 0)}</p>
        </div>
        {prizeTiers.length > 0 && <div className="ff-keno__odds-grid" role="table" aria-label={`${selectedNumbers.length}-spot Keno payouts`}>
          {prizeTiers.map(tier => <div key={tier.hits} role="row" className={round && !isRevealing && round.hitCount === tier.hits ? 'is-achieved' : undefined}><span role="cell">Match {tier.hits}</span><strong role="cell">{formatMoney(wager * tier.multiplier)}</strong></div>)}
        </div>}
      </section>}
      </aside>
    </section>}
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

function formatMoney(value: number): string { return `R${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function readMuted(): boolean { try { return localStorage.getItem('fortuneforge:keno:muted') === 'true' } catch { return false } }
function roundMoney(value: number): number { return Math.round(value * 100) / 100 }
function isSupportedWager(status: KenoStatus, value: number): boolean {
  const steps = (value - status.minimumWager) / status.wagerIncrement
  return value >= status.minimumWager && value <= status.maximumWager && Math.abs(steps - Math.round(steps)) < .000001
}
function supportedWagers(status: KenoStatus, current: number): number[] {
  const last = roundMoney(status.minimumWager + Math.floor((status.maximumWager - status.minimumWager + .000001) / status.wagerIncrement) * status.wagerIncrement)
  return [...new Set([status.minimumWager, last, current, 1, 2, 5, 10, 20])].filter(value => isSupportedWager(status, value)).sort((left, right) => left - right)
}

function playPickEffect(contextRef: { current: AudioContext | null }, action: 'add' | 'remove', tones: Set<OscillatorNode>): void {
  const context = getKenoAudioContext(contextRef)
  if (!context) return
  if (context.state === 'suspended') void context.resume().catch(() => undefined)

  const now = context.currentTime
  if (action === 'add') {
    playKenoTone(context, 235, 125, now, .09, .04, 'sine', tones)
    playKenoTone(context, 520, 300, now + .002, .045, .012, 'triangle', tones)
  } else {
    playKenoTone(context, 190, 105, now, .075, .032, 'sine', tones)
    playKenoTone(context, 390, 245, now + .002, .04, .009, 'triangle', tones)
  }
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
  mutedRef: { current: boolean },
): Promise<number> {
  if (mutedRef.current) return nowMilliseconds()
  const context = getKenoAudioContext(contextRef)
  if (!context) return nowMilliseconds()
  if (context.state === 'suspended') await context.resume().catch(() => undefined)
  const buffer = bufferRef.current ?? await prepareDrawAudio(contextRef, bufferRef, loadRef)
  if (!buffer || mutedRef.current) return nowMilliseconds()

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

function playRoundCompleteEffect(contextRef: { current: AudioContext | null }, won: boolean, delay: number, tones: Set<OscillatorNode>): void {
  const context = getKenoAudioContext(contextRef)
  if (!context) return
  if (context.state === 'suspended') void context.resume().catch(() => undefined)
  const start = context.currentTime + delay
  if (won) {
    playKenoTone(context, 480, 650, start, .13, .038, 'triangle', tones)
    playKenoTone(context, 690, 940, start + .065, .16, .032, 'sine', tones)
  } else {
    playKenoTone(context, 230, 145, start, .12, .026, 'sine', tones)
  }
}

function playKenoTone(context: AudioContext, startFrequency: number, endFrequency: number, start: number, duration: number, volume: number, type: OscillatorType, tones: Set<OscillatorNode>): void {
  const oscillator = context.createOscillator()
  const gain = context.createGain()
  oscillator.type = type
  oscillator.frequency.setValueAtTime(startFrequency, start)
  oscillator.frequency.exponentialRampToValueAtTime(endFrequency, start + duration)
  gain.gain.setValueAtTime(.0001, start)
  gain.gain.exponentialRampToValueAtTime(volume, start + .008)
  gain.gain.exponentialRampToValueAtTime(.0001, start + duration)
  oscillator.connect(gain).connect(context.destination)
  tones.add(oscillator)
  oscillator.onended = () => { tones.delete(oscillator); oscillator.disconnect(); gain.disconnect() }
  oscillator.start(start)
  oscillator.stop(start + duration + .01)
}

function stopTones(tones: Set<OscillatorNode>): void {
  for (const oscillator of tones) { try { oscillator.stop() } catch { /* already ended */ } }
  tones.clear()
}

function nowMilliseconds(): number { return typeof performance === 'undefined' ? Date.now() : performance.now() }

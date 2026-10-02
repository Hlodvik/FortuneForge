import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import {
  BaccaratGatewayError, type BaccaratBetSide, type BaccaratCard,
  type BaccaratGateway, type BaccaratRound, type BaccaratStatus,
} from './contracts'
import { HttpBaccaratGateway } from './httpBaccaratGateway'
import { buildBigRoad, type BaccaratHistoryItem } from './baccaratRoad'
import './baccarat.css'
import './baccaratRoad.css'
import './baccaratViewport.css'

export type BaccaratGameProps = Readonly<{
  gateway?: BaccaratGateway
  playerId?: string
  currencySymbol?: string
  onBalanceChange?: (balance: number) => void
  showTitle?: boolean
}>

const defaultGateway = new HttpBaccaratGateway()
type StoredPendingDeal = Readonly<{ idempotencyKey: string; betSide: BaccaratBetSide; stake: number }>

export function BaccaratGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange, showTitle = true }: BaccaratGameProps) {
  const [status, setStatus] = useState<BaccaratStatus | null>(null)
  const [round, setRound] = useState<BaccaratRound | null>(null)
  const [betSide, setBetSide] = useState<BaccaratBetSide>('player')
  const [stake, setStake] = useState(1)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [history, setHistory] = useState<readonly BaccaratHistoryItem[]>(() => readHistory(playerId))
  const [revealedCards, setRevealedCards] = useState(0)
  const [revealFinished, setRevealFinished] = useState(false)
  const [shoeUsed, setShoeUsed] = useState(() => readShoeUsed(playerId))
  const [shoeRemaining, setShoeRemaining] = useState(() => 416 - readShoeUsed(playerId))
  const [shoeVerified, setShoeVerified] = useState(false)
  const [lastBalance, setLastBalance] = useState<number | null>(null)
  const [activePanel, setActivePanel] = useState<string | null>(null)
  const dealRequestKey = useRef<string | null>(null)
  const pendingBet = useRef<Readonly<{ side: BaccaratBetSide; stake: number }> | null>(null)
  const gameRef = useRef<HTMLElement>(null)
  const historyRef = useRef(history)
  const shoeUsedRef = useRef(shoeUsed)
  const recordedRounds = useRef(new Set(history.map(item => item.roundId)))

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setStake(current => current >= nextStatus.minimumStake && current <= nextStatus.maximumStake && isIncrementAligned(current, nextStatus.minimumStake, nextStatus.stakeIncrement) ? current : nextStatus.minimumStake)
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
    const pendingDeal = playerId ? readPendingDeal(playerId) : null
    if (!pendingDeal) { setRecovery('ready'); return }
    setBetSide(pendingDeal.betSide)
    setStake(pendingDeal.stake)
    const controller = new AbortController()
    setRecovery('recovering')
    void gateway.createRound(pendingDeal.betSide, pendingDeal.stake, {
      signal: controller.signal, idempotencyKey: pendingDeal.idempotencyKey,
    }).then(nextRound => {
      setRound(nextRound)
      setRecovery('ready')
      setError(null)
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      setRecovery('failed')
      setError('Your pending Baccarat deal could not be restored. Retry restoration before starting another hand.')
    })
    return () => controller.abort()
  }, [gateway, playerId, recoveryAttempt])

  useEffect(() => {
    const storedHistory = readHistory(playerId)
    const used = readShoeUsed(playerId)
    historyRef.current = storedHistory
    shoeUsedRef.current = used
    recordedRounds.current = new Set(storedHistory.map(item => item.roundId))
    setHistory(storedHistory)
    setShoeUsed(used)
    setShoeRemaining(416 - used)
    setShoeVerified(false)
  }, [playerId])

  const dealOrder: readonly Readonly<{ side: BaccaratBetSide; index: number }>[] = round ? [
    { side: 'player', index: 0 }, { side: 'banker', index: 0 },
    { side: 'player', index: 1 }, { side: 'banker', index: 1 },
    ...(round.playerCards.length === 3 ? [{ side: 'player' as const, index: 2 }] : []),
    ...(round.bankerCards.length === 3 ? [{ side: 'banker' as const, index: 2 }] : []),
  ] : []
  const completed = round !== null && revealFinished
  useEffect(() => {
    setRevealedCards(0)
    setRevealFinished(false)
    if (!round) return
    setStake(round.stake)
    setBetSide(round.betSide)
    const timers = Array.from({ length: round.playerCards.length + round.bankerCards.length }, (_, index) =>
      window.setTimeout(() => setRevealedCards(index + 1), 160 + index * 220))
    const entranceDuration = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 0 : 280
    timers.push(window.setTimeout(() => setRevealFinished(true), 160 + (timers.length - 1) * 220 + entranceDuration))
    return () => timers.forEach(timer => window.clearTimeout(timer))
  }, [round?.roundId])
  useEffect(() => {
    if (!round || !completed) return
    clearPendingDeal(playerId)
    setLastBalance(round.balance)
    onBalanceChange?.(round.balance)
    const alreadyRecorded = recordedRounds.current.has(round.roundId)
    const cardsUsed = round.playerCards.length + round.bankerCards.length
    const serverShoeUsed = round.shoeCardsUsed ?? (round.shoeCardsRemaining === undefined ? undefined : 416 - round.shoeCardsRemaining)
    if (serverShoeUsed !== undefined || !alreadyRecorded) {
      const used = serverShoeUsed ?? (shoeUsedRef.current >= 312 ? cardsUsed : shoeUsedRef.current + cardsUsed)
      shoeUsedRef.current = used
      setShoeUsed(used)
      setShoeRemaining(round.shoeCardsRemaining ?? 416 - used)
      setShoeVerified(serverShoeUsed !== undefined)
      writeShoeUsed(playerId, used)
    }
    if (alreadyRecorded) return
    recordedRounds.current.add(round.roundId)
    const next = [{ roundId: round.roundId, outcome: round.outcome, cardsUsed, natural: round.endedOnNatural }, ...historyRef.current].slice(0, 40)
    historyRef.current = next
    setHistory(next)
    writeHistory(playerId, next)
  }, [completed, round, onBalanceChange, playerId])
  useEffect(() => {
    if (completed && document.activeElement === document.body) gameRef.current?.querySelector<HTMLButtonElement>('.ff-baccarat__round-actions button:not(:disabled)')?.focus()
  }, [completed])

  const balance = round?.balance ?? lastBalance ?? status?.balance ?? null
  const displayedBalance = lastBalance ?? status?.balance ?? null
  const stakeIsValid = status !== null && stake >= status.minimumStake && stake <= status.maximumStake &&
    isIncrementAligned(stake, status.minimumStake, status.stakeIncrement)
  const stakeAffordable = balance !== null && stake <= balance
  const visibleError = error ?? statusError
  const stakeLocked = busy || recovery !== 'ready' || !status?.available || pendingBet.current !== null

  const act = async (operation: () => Promise<void>) => {
    setBusy(true); setError(null)
    try { await operation() } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }
  const submitDeal = (bet: Readonly<{ side: BaccaratBetSide; stake: number }>, idempotencyKey: string) => {
    pendingBet.current = bet
    storePendingDeal(playerId, { idempotencyKey, betSide: bet.side, stake: bet.stake })
    void act(async () => {
      const nextRound = await gateway.createRound(bet.side, bet.stake, { idempotencyKey })
      dealRequestKey.current = null
      pendingBet.current = null
      setRound(nextRound)
    })
  }
  const deal = () => {
    if (!status?.available || busy || recovery !== 'ready' || (!pendingBet.current && (!stakeIsValid || !stakeAffordable))) return
    const idempotencyKey = dealRequestKey.current ??= createRequestKey()
    submitDeal(pendingBet.current ?? { side: betSide, stake }, idempotencyKey)
  }
  const dealAgain = () => {
    if (busy || recovery !== 'ready' || !status?.available || !round || !completed || round.stake > round.balance) return
    const bet = { side: round.betSide, stake: round.stake }
    setActivePanel(null)
    setLastBalance(round.balance)
    setRound(null); setBetSide(bet.side); setStake(bet.stake)
    const idempotencyKey = createRequestKey()
    dealRequestKey.current = idempotencyKey
    submitDeal(bet, idempotencyKey)
  }
  const newBet = () => {
    if (!round || !completed || busy || recovery !== 'ready') return
    setLastBalance(round.balance)
    setActivePanel(null)
    setRound(null); setError(null)
    window.requestAnimationFrame(() => gameRef.current?.querySelector<HTMLInputElement>('input')?.focus())
  }
  const retryStatus = () => { setStatusError(null); setTableUnavailable(false); setStatusLoadAttempt(attempt => attempt + 1) }
  const retryRestoration = () => { setError(null); setRecoveryAttempt(attempt => attempt + 1) }
  const road = <BaccaratRoad history={history} shoeUsed={shoeUsed} shoeRemaining={shoeRemaining} shoeVerified={shoeVerified} />

  return <main className="ff-baccarat" ref={gameRef} data-embedded={!showTitle} aria-busy={busy || recovery === 'recovering' || (round !== null && !completed)}>
    <header className="ff-baccarat__header">
      {showTitle && <h1>Baccarat</h1>}
      <div className="ff-baccarat__account"><span>Balance</span><strong>{displayedBalance === null ? '—' : formatMoney(displayedBalance, currencySymbol)}</strong></div>
      <InfoPanel label="Baccarat road and shoe" trigger="Road" activePanel={activePanel} onPanelChange={setActivePanel}>{road}</InfoPanel>
      <InfoPanel label="How to play Baccarat" trigger="?" activePanel={activePanel} onPanelChange={setActivePanel}>
        <h2>How to play</h2>
        <p>Choose Player, Banker or Tie. The closer hand to 9 wins. Cards deal automatically under standard Punto Banco rules; a natural 8 or 9 ends the hand.</p>
        <p>Ace counts 1, numbered cards count their value, and tens/court cards count 0. Only the last digit of a hand's total counts.</p>
        <p>Player pays 1:1. Banker pays 0.95:1 after 5% commission. Tie pays 8:1; a tie returns Player and Banker stakes.</p>
        {status && <p>Stake {formatMoney(status.minimumStake, currencySymbol)}–{formatMoney(status.maximumStake, currencySymbol)}, in {formatMoney(status.stakeIncrement, currencySymbol)} increments.</p>}
        <p>The roads summarize completed hands. Session shoe estimates use cards shown on this device until server shoe data arrives.</p>
      </InfoPanel>
    </header>
    <section className="ff-baccarat__table" aria-label="Baccarat table">
      <div className="ff-baccarat__round-head" role="status">{completed && <>
        {round.endedOnNatural && <small>Natural</small>}
        <span>{outcomeLabel(round.outcome)}</span>
        <strong className={'is-' + round.disposition}>{formatSignedMoney(round.profit, currencySymbol)}</strong>
      </>}</div>
      <div className="ff-baccarat__hands">
        <Hand label="Player" cards={round?.playerCards ?? []} count={dealOrder.slice(0, revealedCards).filter(card => card.side === 'player').length} total={completed ? round.playerTotal : null} winner={completed && (round.outcome === 'player' || round.outcome === 'tie')} />
        <Hand label="Banker" cards={round?.bankerCards ?? []} count={dealOrder.slice(0, revealedCards).filter(card => card.side === 'banker').length} total={completed ? round.bankerTotal : null} winner={completed && (round.outcome === 'banker' || round.outcome === 'tie')} />
      </div>
      <div className="ff-baccarat__rail" aria-label="Baccarat controls">
        {!round && (recovery !== 'ready' || (!status && statusError)) ? <div className="ff-baccarat__recovery">
          {recovery === 'recovering' && <span role="status">Restoring your pending Baccarat deal…</span>}
          {recovery === 'failed' && <button className="ff-baccarat__primary" type="button" onClick={retryRestoration}>Retry restoration</button>}
          {!status && statusError && !tableUnavailable && <button className="ff-baccarat__primary" type="button" onClick={retryStatus}>Retry connection</button>}
        </div> : !round ? <>
          <div className="ff-baccarat__bet-sides" role="group" aria-label="Baccarat bet side">
            <BetButton side="player" current={betSide} label="Player" payout="1:1" disabled={stakeLocked} onSelect={setBetSide} />
            <BetButton side="tie" current={betSide} label="Tie" payout="8:1" disabled={stakeLocked} onSelect={setBetSide} />
            <BetButton side="banker" current={betSide} label="Banker" payout="0.95:1" disabled={stakeLocked} onSelect={setBetSide} />
          </div>
          <div className="ff-baccarat__wager-row">
            <StakeField value={stake} min={status?.minimumStake} max={status?.maximumStake} limit={balance ?? undefined} step={status?.stakeIncrement} disabled={stakeLocked} invalid={!stakeIsValid || !stakeAffordable} onChange={setStake} hint={status ? 'Min ' + formatMoney(status.minimumStake, currencySymbol) + ' · Max ' + formatMoney(status.maximumStake, currencySymbol) : undefined} />
            <button className="ff-baccarat__primary" disabled={busy || recovery !== 'ready' || !status?.available || (!pendingBet.current && (!stakeIsValid || !stakeAffordable))} onClick={deal}>{busy ? 'Dealing…' : pendingBet.current ? 'Retry Deal' : 'Deal'}</button>
          </div>
        </> : <>
          <span className="ff-baccarat__ticket">{capitalize(round.betSide)} · {formatMoney(round.stake, currencySymbol)}</span>
          {completed ? <div className="ff-baccarat__round-actions">
            {status?.available ? <button className="ff-baccarat__primary" disabled={busy || recovery !== 'ready' || round.stake > round.balance} onClick={dealAgain}>Deal Again</button> : !status && statusError && !tableUnavailable ? <button className="ff-baccarat__primary" onClick={retryStatus}>Retry connection</button> : <button className="ff-baccarat__primary" disabled>Deal Again</button>}
            <button className="ff-baccarat__secondary" disabled={busy} onClick={newBet}>New Bet</button>
            <InfoPanel label="Baccarat round details" trigger="Details" activePanel={activePanel} onPanelChange={setActivePanel}>
              <h2>Round details</h2>
              <dl className="ff-baccarat__settlement"><div><dt>Bet</dt><dd>{capitalize(round.betSide)} · {formatMoney(round.stake, currencySymbol)}</dd></div><div><dt>Result</dt><dd className={'is-' + round.disposition}>{capitalize(round.disposition)}</dd></div><div><dt>Profit</dt><dd>{formatSignedMoney(round.profit, currencySymbol)}</dd></div><div><dt>Total return</dt><dd>{formatMoney(round.totalReturn, currencySymbol)}</dd></div></dl>
            </InfoPanel>
          </div> : <button className="ff-baccarat__primary" disabled>Dealing…</button>}
        </>}
      </div>
      <div className="ff-baccarat__inline-road">{road}</div>
    </section>
    {visibleError && <p className="ff-baccarat__error" role="alert">{visibleError}</p>}
  </main>
}

function StakeField({ value, min = 0, max, limit, step = 1, disabled, invalid, onChange, hint }: Readonly<{ value: number; min?: number; max?: number; limit?: number; step?: number; disabled: boolean; invalid: boolean; onChange: (value: number) => void; hint?: string }>) {
  const id = useId()
  const [draft, setDraft] = useState(String(value))
  useEffect(() => { if (Number.isFinite(value)) setDraft(String(value)) }, [value])
  const maximum = Math.min(max ?? Infinity, limit ?? Infinity)
  const bump = (direction: -1 | 1) => {
    const steps = (value - min) / step
    const next = Number.isFinite(steps) ? direction > 0 ? Math.floor(steps + 1e-6) + 1 : Math.ceil(steps - 1e-6) - 1 : 0
    const last = min + Math.floor((maximum - min) / step + 1e-6) * step
    onChange(Number(Math.min(last, Math.max(min, min + next * step)).toFixed(2)))
  }
  return <div className="ff-baccarat__stake">
    <label htmlFor={id}>Stake</label>
    <div className="ff-baccarat__stake-edit">
      <button type="button" aria-label="Decrease Baccarat stake" disabled={disabled || maximum < min || value <= min} onClick={() => bump(-1)}>−</button>
      <input id={id} aria-label="Stake" type="number" inputMode="decimal" value={draft} min={min} max={max} step={step} disabled={disabled} aria-invalid={draft !== '' && invalid} onChange={event => { setDraft(event.target.value); onChange(event.target.value === '' ? NaN : Number(event.target.value)) }} />
      <button type="button" aria-label="Increase Baccarat stake" disabled={disabled || maximum < min || value >= maximum} onClick={() => bump(1)}>+</button>
    </div>
    {hint && <small>{hint}</small>}
  </div>
}

function InfoPanel({ label, trigger, children, activePanel, onPanelChange }: Readonly<{ label: string; trigger: string; children: ReactNode; activePanel: string | null; onPanelChange: (label: string | null) => void }>) {
  const open = activePanel === label
  const id = useId()
  const container = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    if (!open) return
    closeButton.current?.focus()
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { onPanelChange(null); if (container.current?.contains(document.activeElement)) button.current?.focus() } }
    const outside = (event: PointerEvent) => { if (!container.current?.contains(event.target as Node)) onPanelChange(null) }
    document.addEventListener('keydown', escape); document.addEventListener('pointerdown', outside)
    return () => { document.removeEventListener('keydown', escape); document.removeEventListener('pointerdown', outside) }
  }, [open, onPanelChange])
  return <div className="ff-baccarat__info" ref={container}>
    <button className="ff-baccarat__secondary" type="button" aria-label={label} aria-expanded={open} aria-controls={id} ref={button} onClick={() => onPanelChange(open ? null : label)}>{trigger}</button>
    {open && <section className="ff-baccarat__info-panel" role="dialog" aria-label={label} id={id}><button type="button" className="ff-baccarat__secondary" aria-label={'Close ' + label.toLowerCase()} ref={closeButton} onClick={() => { onPanelChange(null); button.current?.focus() }}>×</button>{children}</section>}
  </div>
}

function BetButton({ side, current, label, payout, disabled, onSelect }: Readonly<{ side: BaccaratBetSide; current: BaccaratBetSide; label: string; payout: string; disabled: boolean; onSelect: (side: BaccaratBetSide) => void }>) {
  return <button type="button" className={'ff-baccarat__bet is-' + side + (side === current ? ' is-selected' : '')} aria-pressed={side === current} disabled={disabled} onClick={() => onSelect(side)}><strong>{label}</strong><span>{payout}</span></button>
}
function Hand({ label, cards, count, total, winner }: Readonly<{ label: string; cards: readonly BaccaratCard[]; count: number; total: number | null; winner: boolean }>) {
  return <section className={'ff-baccarat__hand' + (winner ? ' is-winner' : '')} aria-label={label + ' hand' + (total === null ? '' : ', total ' + total)}>
    <div className="ff-baccarat__hand-title"><span>{label}</span><strong aria-label={total === null ? label + ' total pending' : label + ' total ' + total}>{total === null ? '—' : total}</strong></div>
    <div className="ff-baccarat__cards">{[0, 1, 2].map(index => <div key={index} className="ff-baccarat__card-slot">
      {index < count && cards[index] ? <CardFace card={cards[index]} /> : index < 2 ? <div className="ff-baccarat__card ff-baccarat__card--back" aria-hidden="true">FF</div> : null}
    </div>)}</div>
  </section>
}
function BaccaratRoad({ history, shoeUsed, shoeRemaining, shoeVerified }: Readonly<{ history: readonly BaccaratHistoryItem[]; shoeUsed: number; shoeRemaining: number; shoeVerified: boolean }>) {
  const player = history.filter(item => item.outcome === 'player').length
  const banker = history.filter(item => item.outcome === 'banker').length
  const ties = history.filter(item => item.outcome === 'tie').length
  const bigRoad = buildBigRoad(history)
  return <section className="ff-baccarat__road" aria-label="Bead plate, Big Road, and shoe progress">
    <div className="ff-baccarat__beads"><small>Bead plate</small><ol>{history.slice(0, 24).reverse().map(item => <li className={'is-' + item.outcome} title={capitalize(item.outcome) + (item.natural ? ' natural' : '')} key={item.roundId}>{item.outcome[0]?.toUpperCase()}</li>)}</ol></div>
    <div className="ff-baccarat__big-road"><small>Big Road</small><div role="img" aria-label={'Big Road showing ' + player + ' Player wins, ' + banker + ' Banker wins, and ' + ties + ' ties'}>{bigRoad.map(cell => <i className={'is-' + cell.outcome} style={{ gridColumn: cell.column + 1, gridRow: cell.row + 1 }} title={capitalize(cell.outcome) + (cell.ties ? ' with ' + cell.ties + ' ties' : '')} key={cell.column + '-' + cell.row}><span>{cell.ties || ''}</span></i>)}</div></div>
    <div className="ff-baccarat__road-stats"><span>Player <b>{player}</b></span><span>Banker <b>{banker}</b></span><span>Tie <b>{ties}</b></span></div>
    <div className="ff-baccarat__shoe"><span>{shoeVerified ? 'Live shoe' : 'Session estimate'}</span><strong>{shoeRemaining} cards</strong><i><b style={{ width: Math.min(100, (shoeUsed / 312) * 100) + '%' }} /></i><small>{shoeVerified ? 'Server shoe reshuffles at 75% penetration.' : 'Tracks cards shown on this device until server shoe data arrives.'}</small></div>
  </section>
}

function CardFace({ card }: Readonly<{ card: BaccaratCard }>) {
  const symbol = suitSymbol(card.suit)
  const red = card.suit === 'diamonds' || card.suit === 'hearts'
  return <div className={`ff-baccarat__card${red ? ' is-red' : ''}`} role="img" aria-label={`${card.rank} of ${card.suit}`}><span className="ff-baccarat__corner">{shortRank(card.rank)}<small>{symbol}</small></span><span className="ff-baccarat__suit" aria-hidden="true">{symbol}</span><span className="ff-baccarat__corner ff-baccarat__corner--reverse">{shortRank(card.rank)}<small>{symbol}</small></span></div>
}

function isIncrementAligned(value: number, minimum: number, increment: number) {
  const steps = (value - minimum) / increment
  return Number.isFinite(steps) && Math.abs(steps - Math.round(steps)) < 1e-9
}
function shortRank(rank: BaccaratCard['rank']) { return ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[rank] }
function suitSymbol(suit: BaccaratCard['suit']) { return ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[suit] }
function outcomeLabel(outcome: BaccaratRound['outcome']) { return outcome === 'tie' ? 'Tie' : `${capitalize(outcome)} wins` }
function capitalize(value: string) { return value[0]?.toUpperCase() + value.slice(1) }
function formatMoney(value: number, currencySymbol: string) { return `${value < 0 ? '-' : ''}${currencySymbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function formatSignedMoney(value: number, currencySymbol: string) { return `${value > 0 ? '+' : ''}${formatMoney(value, currencySymbol)}` }
function messageForError(reason: unknown) { return reason instanceof BaccaratGatewayError ? reason.message : 'The Baccarat table is unavailable.' }
function isTableUnavailable(reason: unknown) { return reason instanceof BaccaratGatewayError && reason.code === 'baccarat-disabled' }
function createRequestKey() { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `baccarat-${random}` }
function pendingDealKey(playerId: string) { return `fortuneforge:baccarat:pending:${playerId}` }
function readPendingDeal(playerId: string): StoredPendingDeal | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingDealKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const deal = value as Record<string, unknown>
    if (typeof deal.idempotencyKey === 'string' && typeof deal.stake === 'number' && (deal.betSide === 'player' || deal.betSide === 'banker' || deal.betSide === 'tie')) {
      return { idempotencyKey: deal.idempotencyKey, betSide: deal.betSide, stake: deal.stake }
    }
  } catch { /* session storage is optional */ }
  return null
}
function storePendingDeal(playerId: string | undefined, deal: StoredPendingDeal) { if (!playerId) return; try { sessionStorage.setItem(pendingDealKey(playerId), JSON.stringify(deal)) } catch { /* session storage is optional */ } }
function clearPendingDeal(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingDealKey(playerId)) } catch { /* session storage is optional */ } }
function historyKey(playerId: string | undefined) { return `fortuneforge:baccarat:history:${playerId ?? 'guest'}` }
function shoeKey(playerId: string | undefined) { return `fortuneforge:baccarat:shoe:${playerId ?? 'guest'}` }
function readHistory(playerId: string | undefined): readonly BaccaratHistoryItem[] { try { const value = JSON.parse(localStorage.getItem(historyKey(playerId)) ?? '[]'); return Array.isArray(value) ? value.filter(isHistoryItem).slice(0, 40) : [] } catch { return [] } }
function writeHistory(playerId: string | undefined, history: readonly BaccaratHistoryItem[]): void { try { localStorage.setItem(historyKey(playerId), JSON.stringify(history)) } catch { /* optional storage */ } }
function isHistoryItem(value: unknown): value is BaccaratHistoryItem { if (!value || typeof value !== 'object') return false; const item = value as Record<string, unknown>; return typeof item.roundId === 'string' && (item.outcome === 'player' || item.outcome === 'banker' || item.outcome === 'tie') && Number.isInteger(item.cardsUsed) && typeof item.natural === 'boolean' }
function readShoeUsed(playerId: string | undefined): number { try { const value = Number(localStorage.getItem(shoeKey(playerId))); return Number.isInteger(value) && value >= 0 && value <= 416 ? value : 0 } catch { return 0 } }
function writeShoeUsed(playerId: string | undefined, used: number): void { try { localStorage.setItem(shoeKey(playerId), String(used)) } catch { /* optional storage */ } }

import { useCallback, useEffect, useId, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { InGameShell } from '../../../components/InGameShell'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import {
  ThreeCardPokerRequestError, cancelThreeCardPokerQueue, getThreeCardPokerSnapshot,
  getThreeCardPokerStatus, isUncertainThreeCardPokerFailure, joinThreeCardPokerQueue,
  leaveThreeCardPokerTable, postThreeCardPokerAction, postThreeCardPokerWager,
  sitOutThreeCardPokerRound, stableThreeCardPokerMutation, toThreeCardPokerPlayingCard,
  validThreeCardPokerWager,
  type PendingThreeCardPokerMutation, type ThreeCardPokerAction, type ThreeCardPokerHand,
  type ThreeCardPokerMutationResponse, type ThreeCardPokerPlaySession, type ThreeCardPokerQueueSession,
  type ThreeCardPokerSeat, type ThreeCardPokerSession, type ThreeCardPokerStatus,
} from '../../../games/cards/threeCardPoker/threeCardPokerApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import { playCardAudio, useCardAudioClick } from '../../../games/cards/shared/cardAudio'
import '../../../games/cards/shared/playingCards.css'
import '../blackjack/blackjack.css'
import './threeCardPoker.css'

type Availability = Readonly<{ kind: 'loading' }>
  | Readonly<{ kind: 'ready'; status: ThreeCardPokerStatus; session: ThreeCardPokerSession }>
  | Readonly<{ kind: 'error' | 'disabled'; message: string }>

export function ThreeCardPokerPage({ account }: { account: AccountSummary }) {
  const [availability, setAvailability] = useState<Availability>({ kind: 'loading' })
  const [balanceCredits, setBalanceCredits] = useState(account.balances.slotsCredits)
  const [ante, setAnte] = useState(5)
  const [pairPlus, setPairPlus] = useState(0)
  const [pending, setPending] = useState<PendingThreeCardPokerMutation | null>(null)
  const [busy, setBusy] = useState(false)
  const [requestError, setRequestError] = useState<string | null>(null)
  const [now, setNow] = useState(Date.now)
  const refreshInFlightRef = useRef<Promise<void> | null>(null)
  const onCardAudioClick = useCardAudioClick()

  const load = useCallback(async (signal?: AbortSignal) => {
    setAvailability({ kind: 'loading' })
    try {
      const [status, snapshot] = await Promise.all([getThreeCardPokerStatus(signal), getThreeCardPokerSnapshot(signal)])
      setAnte((current) => validThreeCardPokerWager(current, status) ? current : status.minimumWager)
      setPairPlus((current) => validThreeCardPokerWager(current, status, true) && status.pairPlusAllowed ? current : 0)
      setAvailability(status.available ? { kind: 'ready', status, session: snapshot.session } : { kind: 'disabled', message: 'The Three Card Poker table is currently unavailable.' })
      setBalanceCredits(snapshot.balanceCredits)
      setPending(null)
      setRequestError(null)
    } catch (error) {
      if (signal?.aborted) return
      const disabled = error instanceof ThreeCardPokerRequestError && error.code === 'three-card-poker-table-disabled'
      setAvailability({ kind: disabled ? 'disabled' : 'error', message: disabled ? error.message : 'The Three Card Poker table could not be reached. No wager was accepted.' })
    }
  }, [])

  const refreshSession = useCallback(() => {
    if (refreshInFlightRef.current) return refreshInFlightRef.current
    const refresh = (async () => {
      try {
        const snapshot = await getThreeCardPokerSnapshot()
        setAvailability((current) => current.kind === 'ready' ? { ...current, session: snapshot.session } : current)
        setBalanceCredits(snapshot.balanceCredits)
        setPending(null)
      } catch {
        // Preserve the last authoritative snapshot until a later poll succeeds.
      } finally { refreshInFlightRef.current = null }
    })()
    refreshInFlightRef.current = refresh
    return refresh
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])
  useEffect(() => {
    if (availability.kind !== 'ready' || availability.session.kind === 'idle' || busy || pending) return
    const table = availability.session.kind === 'table' ? availability.session.table : null
    const interval = table?.phase === 'dealing' ? 100 : table?.transition || table?.phase === 'dealer' ? 250 : 1_250
    let stopped = false
    let timer = 0
    const poll = async () => { await refreshSession(); if (!stopped) timer = window.setTimeout(poll, interval) }
    const refresh = () => void refreshSession()
    timer = window.setTimeout(poll, interval)
    window.addEventListener('focus', refresh)
    window.addEventListener('online', refresh)
    return () => {
      stopped = true
      window.clearTimeout(timer)
      window.removeEventListener('focus', refresh)
      window.removeEventListener('online', refresh)
    }
  }, [availability, busy, pending, refreshSession])
  useEffect(() => {
    if (availability.kind !== 'ready' || availability.session.kind === 'idle') return
    const timer = window.setInterval(() => setNow(Date.now()), 250)
    return () => window.clearInterval(timer)
  }, [availability])

  const runMutation = async (fingerprint: string, request: (key: string) => Promise<ThreeCardPokerMutationResponse>) => {
    if (pending && pending.fingerprint !== fingerprint) {
      setRequestError('Retry the same pending request or refresh the table before choosing another action.')
      return
    }
    const active = stableThreeCardPokerMutation(pending, fingerprint)
    setPending(active)
    setRequestError(null)
    setBusy(true)
    try {
      const [result] = await Promise.all([request(active.idempotencyKey), fingerprint.startsWith('join:') ? new Promise((resolve) => window.setTimeout(resolve, 1_000)) : Promise.resolve()])
      setAvailability((current) => current.kind === 'ready' ? { ...current, session: result.session } : current)
      setBalanceCredits(result.balanceCredits)
      setPending(null)
    } catch (error) {
      if (error instanceof ThreeCardPokerRequestError && error.code === 'three-card-poker-table-state-conflict') {
        setPending(null)
        setRequestError('The table changed before that request arrived. Restoring the latest state…')
        void refreshSession()
      } else {
        if (!isUncertainThreeCardPokerFailure(error)) setPending(null)
        setRequestError(error instanceof Error ? error.message : 'The table could not complete the request.')
      }
    } finally { setBusy(false) }
  }
  const ready = availability.kind === 'ready' ? availability.session : null
  const join = () => ready?.kind === 'idle' && void runMutation(`join:${ready.version}`, (key) => joinThreeCardPokerQueue(ready.version, key))
  const cancel = (session: ThreeCardPokerQueueSession) => void runMutation(`cancel:${session.ticketId}:${session.version}`, (key) => cancelThreeCardPokerQueue(session.ticketId, session.version, key))
  const wager = (session: ThreeCardPokerPlaySession) => void runMutation(`wager:${session.table.tableId}:${ante}:${pairPlus}:${session.version}`, (key) => postThreeCardPokerWager(session.table.tableId, ante, pairPlus, session.version, key))
  const sitOut = (session: ThreeCardPokerPlaySession) => void runMutation(`sit-out:${session.table.tableId}:${session.version}`, (key) => sitOutThreeCardPokerRound(session.table.tableId, session.version, key))
  const act = (session: ThreeCardPokerPlaySession, action: ThreeCardPokerAction) => void runMutation(`action:${session.table.tableId}:${action}:${session.version}`, (key) => postThreeCardPokerAction(session.table.tableId, action, session.version, key))
  const leave = (session: ThreeCardPokerPlaySession) => void runMutation(`leave:${session.table.tableId}:${session.version}`, (key) => leaveThreeCardPokerTable(session.table.tableId, session.version, key))
  const navbarAccount = { ...account, balances: { ...account.balances, slotsCredits: balanceCredits } }
  return <InGameShell account={navbarAccount} title="Three Card Poker" theme="cards" className="blackjack-game-shell" bodyClassName="blackjack-shell-body"
    actions={availability.kind === 'ready' ? <ThreeCardPokerHowToPlay status={availability.status} /> : undefined}>
    <div className="blackjack-page blackjack-table-page three-card-poker-page" onClickCapture={onCardAudioClick}>
      {requestError && <div className="blackjack-error" role="alert"><span>{requestError}</span><button type="button" disabled={busy} onClick={() => { setPending(null); void load() }}>Refresh table</button></div>}
      <ThreeCardPokerContent availability={availability} balanceCredits={balanceCredits} ante={ante} pairPlus={pairPlus} busy={busy} pending={pending} now={now}
        onAnteChange={setAnte} onPairPlusChange={setPairPlus} onJoin={join} onCancel={cancel} onWager={wager} onSitOut={sitOut} onAction={act} onLeave={leave} onRefresh={() => void load()} />
    </div>
  </InGameShell>
}

type ContentProps = Readonly<{
  availability: Availability; balanceCredits: number; ante: number; pairPlus: number
  busy: boolean; pending: PendingThreeCardPokerMutation | null; now: number
  onAnteChange: (value: number) => void; onPairPlusChange: (value: number) => void
  onJoin: () => void; onCancel: (session: ThreeCardPokerQueueSession) => void
  onWager: (session: ThreeCardPokerPlaySession) => void; onSitOut: (session: ThreeCardPokerPlaySession) => void
  onAction: (session: ThreeCardPokerPlaySession, action: ThreeCardPokerAction) => void
  onLeave: (session: ThreeCardPokerPlaySession) => void; onRefresh: () => void
}>
export function ThreeCardPokerContent(props: ContentProps) {
  const { availability } = props
  if (availability.kind === 'loading') return <main className="blackjack-main blackjack-state" role="status">Opening the live Three Card Poker table…</main>
  if (availability.kind !== 'ready') return <main className="blackjack-main blackjack-state" role={availability.kind === 'error' ? 'alert' : 'status'}><h1>{availability.kind === 'error' ? 'The table is offline.' : 'Three Card Poker is unavailable.'}</h1><p>{availability.message}</p><button type="button" onClick={props.onRefresh}>Try again</button></main>
  const { session, status } = availability
  if (session.kind === 'idle') {
    const retry = props.pending?.fingerprint === `join:${session.version}`
    if (props.busy && retry) return <FindingTable />
    return <main className="blackjack-main blackjack-lobby"><section className="blackjack-title"><h1>Three Card Poker</h1></section><button className="blackjack-primary" type="button" disabled={props.busy || (props.pending !== null && !retry)} onClick={props.onJoin}>{retry ? 'Retry same request' : 'Join live table'}</button></main>
  }
  if (session.kind === 'queue') return <FindingTable><button className="blackjack-secondary" type="button" disabled={props.busy} onClick={() => props.onCancel(session)}>Cancel</button></FindingTable>
  return <TablePanel {...props} status={status} session={session} />
}
function FindingTable({ children }: { children?: ReactNode }) {
  return <main className="blackjack-main blackjack-lobby blackjack-finding" role="status"><span className="blackjack-finding__spinner" aria-hidden="true" /><section className="blackjack-title"><h1>Finding table…</h1></section>{children}</main>
}
function TablePanel(props: ContentProps & { status: ThreeCardPokerStatus; session: ThreeCardPokerPlaySession }) {
  const { table } = props.session
  const current = table.seats.find((seat) => seat.isCurrentPlayer)
  const betting = table.phase === 'betting'
  const wagerTurn = betting && table.transition === 'human-wager' && table.activeSeat === current?.seat
  const actionTurn = table.phase === 'active' && table.transition === null && table.activeSeat === current?.seat
  const seats = new Map(table.seats.map((seat) => [seat.seat, seat]))
  const count = table.dealer.cards.length + table.seats.reduce((total, seat) => total + seat.hand.cards.length, 0)
  const previousCount = useRef(count)
  const previousPhase = useRef(table.phase)
  useEffect(() => {
    if (count > previousCount.current && document.visibilityState === 'visible') playCardAudio('deal')
    previousCount.current = count
    if (table.phase !== previousPhase.current && document.visibilityState === 'visible') {
      if (table.phase === 'dealing') playCardAudio('shuffle')
      else if (table.phase === 'settled') playCardAudio('collect')
    }
    previousPhase.current = table.phase
  }, [count, table.phase])
  const blocked = (fingerprint: string) => props.busy || (props.pending !== null && props.pending.fingerprint !== fingerprint)
  const currentTimer = actionTurn ? countdown(table.actionDeadlineAtUtc, props.now) : null
  const nextRound = table.transition === 'next-round-countdown' ? Math.ceil(Math.max(0, Date.parse(table.nextTransitionAtUtc ?? '') - props.now) / 1_000) : null
  return <main className="blackjack-main blackjack-game"><section className="blackjack-table" aria-label="Live Three Card Poker table" data-phase={table.phase}>
    <div className="blackjack-playfield">
      <div className={`blackjack-dealer${table.phase === 'dealer' ? ' is-active' : ''}`}><Hand label="Dealer" hand={betting ? emptyHand : table.dealer} scope="dealer" /></div>
      <div className="three-card-poker-felt" aria-hidden="true"><strong>Three Card Poker</strong><span>{props.status.dealerRule}</span></div>
      <div className="blackjack-semicircle" aria-label="Player seats">{centeredSeatNumbers(props.status.tableCapacity, current?.seat).map((number, index) => {
        const seat = seats.get(number)
        return <div className={`blackjack-seat-slot blackjack-seat-slot--${index + 1}${seat?.isCurrentPlayer ? ' is-current-slot' : ''}`} key={seat?.seatId ?? number}>
          {seat ? <Seat seat={seat} betting={betting} active={seat.seat === table.activeSeat} timer={!seat.isCurrentPlayer && seat.seat === table.activeSeat && table.phase === 'active' && !table.transition ? countdown(table.actionDeadlineAtUtc, props.now) : null} /> : <div className="blackjack-seat blackjack-seat--open"><strong>Open seat</strong></div>}
        </div>
      })}</div>
    </div>
    {nextRound !== null && <div className="blackjack-next-round" role="status">Next round {nextRound}</div>}
    <div className="blackjack-actions" aria-label="Three Card Poker controls">
      <div className="blackjack-balance-bubble" aria-label={`Balance R${props.balanceCredits.toFixed(2)}`} aria-live="polite"><small>Balance</small><strong>R{props.balanceCredits.toFixed(2)}</strong></div>
      {wagerTurn && <WagerInput status={props.status} balance={props.balanceCredits} ante={props.ante} pairPlus={props.pairPlus} locked={props.busy || props.pending !== null}
        canSubmit={!blocked(`wager:${table.tableId}:${props.ante}:${props.pairPlus}:${props.session.version}`)} canSitOut={!blocked(`sit-out:${table.tableId}:${props.session.version}`)} timer={countdown(table.wagerDeadlineAtUtc, props.now)} onAnteChange={props.onAnteChange} onPairPlusChange={props.onPairPlusChange} onSubmit={() => props.onWager(props.session)} onSitOut={() => props.onSitOut(props.session)} />}
      {table.phase === 'active' && (['play', 'fold'] as const).map((action) => <button key={action} type="button" disabled={!actionTurn || !table.legalActions.includes(action) || blocked(`action:${table.tableId}:${action}:${props.session.version}`)} onClick={() => props.onAction(props.session, action)}>{action === 'play' ? `Play R${(current?.ante ?? 0).toFixed(2)}` : 'Fold'}</button>)}
      {currentTimer && <time className="blackjack-action-timer" aria-label={`Time remaining ${currentTimer}`}>{currentTimer}</time>}
    </div>
    <button className="blackjack-leave blackjack-leave--corner" type="button" disabled={blocked(`leave:${table.tableId}:${props.session.version}`)} onClick={() => props.onLeave(props.session)}>Leave table</button>
  </section></main>
}
function Seat({ seat, betting, active, timer }: { seat: ThreeCardPokerSeat; betting: boolean; active: boolean; timer: string | null }) {
  const net = seat.payout - seat.totalWager
  return <article className={`blackjack-seat${seat.isCurrentPlayer ? ' is-current' : ''}${active ? ' is-active' : ''}${!betting && seat.outcome && net > 0 ? ' is-winner' : ''}`}>
    <div className="blackjack-seat__name"><strong>{seat.displayName}</strong><small>{betting ? '' : formatLabel(seat.outcome ?? seat.lastAction ?? '')}</small>{timer && <time className="blackjack-seat__timer">{timer}</time>}</div>
    <div className="blackjack-seat__hands"><div><Hand label="" hand={betting ? emptyHand : seat.hand} scope={seat.seatId} compact={!seat.isCurrentPlayer} /></div></div>
    {seat.status === 'joining-next-round' && <span className="blackjack-seat__joining">Joining next round</span>}
    <div className="blackjack-seat__money"><span>Ante R{seat.ante.toFixed(2)}</span>{seat.pairPlus > 0 && <span>Pair+ R{seat.pairPlus.toFixed(2)}</span>}{!betting && seat.playWager > 0 && <span>Play R{seat.playWager.toFixed(2)}</span>}</div>
    {!betting && seat.outcome && <div className="blackjack-seat__money"><strong className={net >= 0 ? 'is-positive' : 'is-negative'}>{net >= 0 ? '+' : '−'}R{Math.abs(net).toFixed(2)}</strong>{seat.anteBonusPayout > 0 && <small>Ante bonus R{seat.anteBonusPayout.toFixed(2)}</small>}{seat.pairPlusPayout > 0 && <small>Pair Plus R{seat.pairPlusPayout.toFixed(2)}</small>}</div>}
  </article>
}
const emptyHand: ThreeCardPokerHand = { cards: [], rank: null }
function Hand({ label, hand, scope, compact = false }: { label: string; hand: ThreeCardPokerHand; scope: string; compact?: boolean }) {
  return <div className={`blackjack-hand${compact ? ' blackjack-hand--compact' : ''}`} style={{ '--blackjack-fan-width': 1.6 + (compact ? .4 : .75) * Math.max(0, hand.cards.length - 1) } as CSSProperties}>
    {(label || hand.rank) && <div className="blackjack-hand__heading">{label && <h2>{label}</h2>}{hand.rank && <small>{formatLabel(hand.rank)}</small>}</div>}
    <div className="blackjack-hand__cards">{hand.cards.map((card, index) => {
      const playingCard = toThreeCardPokerPlayingCard(card, index, scope)
      return <span className="ff-card-slot blackjack-card-enter" key={`${scope}-${index}`}><PlayingCard card={playingCard ?? { id: `${scope}-hidden-${index}`, suit: 'spades', rank: 1 }} faceDown={playingCard === null} /></span>
    })}</div>
  </div>
}
function WagerInput({ status, balance, ante, pairPlus, locked, canSubmit, canSitOut, timer, onAnteChange, onPairPlusChange, onSubmit, onSitOut }: {
  status: ThreeCardPokerStatus; balance: number; ante: number; pairPlus: number; locked: boolean; canSubmit: boolean; canSitOut: boolean; timer: string
  onAnteChange: (value: number) => void; onPairPlusChange: (value: number) => void; onSubmit: () => void; onSitOut: () => void
}) {
  const valid = validThreeCardPokerWager(ante, status) && validThreeCardPokerWager(pairPlus, status, true) && ante + pairPlus <= balance
  return <div className="blackjack-wager-wrap three-card-poker-wager-wrap"><div className="blackjack-wager-heading"><span>Round wagers</span><small>Min R{status.minimumWager.toFixed(2)} · Max R{status.maximumWager.toFixed(2)}</small></div><div className="blackjack-wager three-card-poker-wager" role="group" aria-label="Round wagers">
    <label><span>Ante R</span><input type="number" aria-label="Ante amount" inputMode="decimal" min={status.minimumWager} max={status.maximumWager} step={status.wagerIncrement} value={Number.isFinite(ante) ? ante : ''} disabled={locked} aria-invalid={!validThreeCardPokerWager(ante, status)} onChange={(event) => onAnteChange(event.target.value === '' ? NaN : Number(event.target.value))} /></label>
    {status.pairPlusAllowed && <label><span>Pair Plus R</span><input type="number" aria-label="Pair Plus amount" inputMode="decimal" min={0} max={status.maximumWager} step={status.wagerIncrement} value={Number.isFinite(pairPlus) ? pairPlus : ''} disabled={locked} aria-invalid={!validThreeCardPokerWager(pairPlus, status, true)} onChange={(event) => onPairPlusChange(event.target.value === '' ? NaN : Number(event.target.value))} /></label>}
    <button className="blackjack-wager-submit" type="button" disabled={!canSubmit || !valid} onClick={onSubmit}>Wager</button><button className="blackjack-wager-sit-out" type="button" disabled={!canSitOut} onClick={onSitOut}>Sit out</button><time aria-label={`Time remaining ${timer}`}>{timer}</time>
  </div><small className="three-card-poker-reserve">Play adds an equal ante wager.</small></div>
}
export function ThreeCardPokerHowToPlay({ status, defaultOpen = false }: { status: ThreeCardPokerStatus; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  const id = useId()
  const container = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const close = () => { setOpen(false); trigger.current?.focus() }
  useEffect(() => {
    if (!open) return
    closeButton.current?.focus()
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpen(false); trigger.current?.focus() } }
    const outside = (event: PointerEvent) => { if (!container.current?.contains(event.target as Node)) setOpen(false) }
    document.addEventListener('keydown', escape)
    document.addEventListener('pointerdown', outside)
    return () => { document.removeEventListener('keydown', escape); document.removeEventListener('pointerdown', outside) }
  }, [open])
  return <div className="blackjack-help" ref={container}><button className="blackjack-help__button" ref={trigger} type="button" aria-label="How to play Three Card Poker" aria-expanded={open} aria-controls={id} onClick={() => setOpen((value) => !value)}>?</button>{open && <section className="blackjack-help__panel" id={id} role="dialog" aria-label="How to play Three Card Poker">
    <header><div><small>Three Card Poker guide</small><h2>How to play</h2></div><button ref={closeButton} type="button" aria-label="Close how to play" onClick={close}>×</button></header>
    <p>Place an Ante to receive three cards. Play adds a wager equal to your Ante and compares your hand with the dealer. Fold gives up your Ante.</p>
    <div className="blackjack-help__actions"><span><strong>Play</strong>Challenge the dealer with your three-card hand.</span><span><strong>Fold</strong>End the hand without a Play wager.</span>{status.pairPlusAllowed && <span><strong>Pair Plus</strong>An optional side wager on a pair or better, regardless of the dealer. {status.pairPlusPaysOnFold ? 'It remains eligible when you fold.' : 'You must Play to remain eligible.'}</span>}<span><strong>Ante bonus</strong>A straight or better pays a bonus when you Play.</span></div>
    <p>Hand order: straight flush, three of a kind, straight, flush, pair, high card. A straight beats a flush in Three Card Poker.</p>
    <dl className="blackjack-help__rules"><div><dt>Dealer</dt><dd>{status.dealerRule}</dd></div><div><dt>No qualification</dt><dd>Ante wins 1:1; Play is returned</dd></div><div><dt>Dealer comparison</dt><dd>Ante and Play win 1:1; ties push</dd></div><div><dt>Ante bonus</dt><dd>{status.anteBonusPaytable}</dd></div>{status.pairPlusAllowed && <div><dt>Pair Plus</dt><dd>{status.pairPlusPaytable}</dd></div>}<div><dt>Turn</dt><dd>{status.actionDeadlineSeconds} seconds</dd></div></dl>
  </section>}</div>
}
function centeredSeatNumbers(capacity: number, current?: number): number[] {
  const center = Math.floor(capacity / 2)
  return Array.from({ length: capacity }, (_, index) => ((current ?? center) + index - center + capacity) % capacity)
}
function countdown(deadline: string | null, now: number): string { return deadline === null ? 'Waiting' : `${Math.ceil(Math.max(0, Date.parse(deadline) - now) / 1_000)}s` }
function formatLabel(value: string): string { return value.replaceAll('-', ' ').replace(/\b\w/g, (character) => character.toUpperCase()) }

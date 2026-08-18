import { useCallback, useEffect, useState } from 'react'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import {
  BlackjackTableRequestError,
  cancelBlackjackTableQueue,
  getBlackjackTableSession,
  getBlackjackTableStatus,
  isUncertainBlackjackTableFailure,
  joinBlackjackTableQueue,
  leaveBlackjackTable,
  postBlackjackTableAction,
  postBlackjackTableWager,
  stableBlackjackTableMutation,
  toBlackjackTablePlayingCard,
  type BlackjackTable,
  type BlackjackTableAction,
  type BlackjackTableHand,
  type BlackjackTableMutationResponse,
  type BlackjackTablePlayerHand,
  type BlackjackTablePlaySession,
  type BlackjackTableQueueSession,
  type BlackjackTableSeat,
  type BlackjackTableSession,
  type BlackjackTableStatus,
  type PendingBlackjackTableMutation,
} from '../../../games/cards/blackjack/blackjackTableApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import '../../../games/cards/shared/playingCards.css'
import { CardRoomNavigation } from '../CardRoomNavigation'
import './blackjack.css'

type Availability =
  | Readonly<{ kind: 'loading' }>
  | Readonly<{ kind: 'ready'; status: BlackjackTableStatus; session: BlackjackTableSession }>
  | Readonly<{ kind: 'disabled'; message: string }>
  | Readonly<{ kind: 'error'; message: string }>

export function BlackjackTablePage({ account }: { account: AccountSummary }) {
  const [availability, setAvailability] = useState<Availability>({ kind: 'loading' })
  const [balanceCredits, setBalanceCredits] = useState(account.balances.slotsCredits)
  const [wager, setWager] = useState(5)
  const [pending, setPending] = useState<PendingBlackjackTableMutation | null>(null)
  const [busy, setBusy] = useState(false)
  const [requestError, setRequestError] = useState<string | null>(null)
  const [now, setNow] = useState(() => Date.now())

  const load = useCallback(async (quiet = false, signal?: AbortSignal) => {
    if (!quiet) setAvailability({ kind: 'loading' })
    try {
      const [status, session] = await Promise.all([
        getBlackjackTableStatus(signal),
        getBlackjackTableSession(signal),
      ])
      setWager((current) => validWager(current, status) ? current : status.minimumWager)
      setAvailability({ kind: 'ready', status, session })
      setPending(null)
      setRequestError(null)
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (error instanceof BlackjackTableRequestError && error.code === 'blackjack-table-disabled') {
        setAvailability({ kind: 'disabled', message: error.message })
        setPending(null)
        return
      }
      if (!quiet) setAvailability({ kind: 'error', message: 'The Blackjack table could not be reached. No wager was accepted.' })
    }
  }, [])

  const refreshSession = useCallback(async () => {
    try {
      const session = await getBlackjackTableSession()
      setAvailability((current) => current.kind === 'ready' ? { ...current, session } : current)
      setPending(null)
    } catch {
      // Retain the latest server snapshot until a later poll succeeds.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(false, controller.signal)
    return () => controller.abort()
  }, [load])

  useEffect(() => {
    if (availability.kind !== 'ready'
      || (availability.session.kind !== 'queue' && availability.session.kind !== 'table')
      || busy || pending !== null) return
    const table = availability.session.kind === 'table' ? availability.session.table : null
    const interval = table?.transition || table?.phase === 'dealer' ? 350 : 1_250
    const refresh = () => void refreshSession()
    const timer = window.setInterval(refresh, interval)
    window.addEventListener('focus', refresh)
    window.addEventListener('online', refresh)
    return () => {
      window.clearInterval(timer)
      window.removeEventListener('focus', refresh)
      window.removeEventListener('online', refresh)
    }
  }, [availability, busy, pending, refreshSession])

  useEffect(() => {
    if (availability.kind !== 'ready' || availability.session.kind !== 'table') return
    const timer = window.setInterval(() => setNow(Date.now()), 250)
    return () => window.clearInterval(timer)
  }, [availability])

  const runMutation = async (
    fingerprint: string,
    request: (key: string) => Promise<BlackjackTableMutationResponse>,
  ) => {
    if (pending !== null && pending.fingerprint !== fingerprint) {
      setRequestError('Retry the same pending request or refresh the table before choosing another action.')
      return
    }
    const active = stableBlackjackTableMutation(pending, fingerprint)
    setPending(active)
    setRequestError(null)
    setBusy(true)
    try {
      const mutation = await request(active.idempotencyKey)
      setAvailability((current) => current.kind === 'ready' ? { ...current, session: mutation.session } : current)
      setBalanceCredits(mutation.balanceCredits)
      setPending(null)
    } catch (error) {
      if (error instanceof BlackjackTableRequestError && error.code === 'blackjack-table-state-conflict') {
        setPending(null)
        setRequestError('The table changed before that request arrived. Restoring the latest state…')
        void refreshSession()
      } else {
        if (!isUncertainBlackjackTableFailure(error)) setPending(null)
        setRequestError(error instanceof Error ? error.message : 'The Blackjack table could not complete the request.')
      }
    } finally {
      setBusy(false)
    }
  }

  const ready = availability.kind === 'ready' ? availability.session : null
  const join = () => ready?.kind === 'idle' && void runMutation(
    `join:${ready.version}`,
    (key) => joinBlackjackTableQueue(ready.version, key),
  )
  const cancel = (queue: BlackjackTableQueueSession) => void runMutation(
    `cancel:${queue.ticketId}:${queue.version}`,
    (key) => cancelBlackjackTableQueue(queue.ticketId, queue.version, key),
  )
  const chooseWager = (session: BlackjackTablePlaySession) => void runMutation(
    `wager:${session.table.tableId}:${wager}:${session.version}`,
    (key) => postBlackjackTableWager(session.table.tableId, wager, session.version, key),
  )
  const act = (session: BlackjackTablePlaySession, action: BlackjackTableAction) => void runMutation(
    `action:${session.table.tableId}:${action}:${session.version}`,
    (key) => postBlackjackTableAction(session.table.tableId, action, session.version, key),
  )
  const leave = (session: BlackjackTablePlaySession) => void runMutation(
    `leave:${session.table.tableId}:${session.version}`,
    (key) => leaveBlackjackTable(session.table.tableId, session.version, key),
  )

  return (
    <div className="blackjack-page blackjack-table-page">
      <CardRoomNavigation
        playerName={account.playerName}
        balanceCredits={balanceCredits}
        onBalanceChange={setBalanceCredits}
      />
      {requestError && (
        <div className="blackjack-error" role="alert">
          <span>{requestError}</span>
          <button type="button" onClick={() => { setPending(null); void load() }}>Refresh table</button>
        </div>
      )}
      <BlackjackTableContent
        availability={availability}
        balanceCredits={balanceCredits}
        wager={wager}
        busy={busy}
        pending={pending}
        now={now}
        onWagerChange={setWager}
        onJoin={join}
        onCancel={cancel}
        onWager={chooseWager}
        onAction={act}
        onLeave={leave}
        onRefresh={() => void load()}
      />
    </div>
  )
}

type ContentProps = Readonly<{
  availability: Availability
  balanceCredits: number
  wager: number
  busy: boolean
  pending: PendingBlackjackTableMutation | null
  now: number
  onWagerChange: (wager: number) => void
  onJoin: () => void
  onCancel: (queue: BlackjackTableQueueSession) => void
  onWager: (table: BlackjackTablePlaySession) => void
  onAction: (table: BlackjackTablePlaySession, action: BlackjackTableAction) => void
  onLeave: (table: BlackjackTablePlaySession) => void
  onRefresh: () => void
}>

export function BlackjackTableContent(props: ContentProps) {
  if (props.availability.kind === 'loading') return <main className="blackjack-main blackjack-state" role="status">Opening the live Blackjack table…</main>
  if (props.availability.kind === 'disabled') return <main className="blackjack-main blackjack-state" role="status"><h1>Blackjack table is coming soon.</h1><p>{props.availability.message}</p></main>
  if (props.availability.kind === 'error') return <main className="blackjack-main blackjack-state" role="alert"><h1>The table is offline.</h1><p>{props.availability.message}</p><button type="button" onClick={props.onRefresh}>Try again</button></main>

  const { status, session } = props.availability
  if (session.kind === 'idle') {
    const fingerprint = `join:${session.version}`
    const retrying = props.pending?.fingerprint === fingerprint
    return (
      <main className="blackjack-main blackjack-lobby">
        <section className="blackjack-title"><p>Free to join</p><h1>Blackjack</h1><span>{status.dealerRule} · Blackjack pays {status.blackjackPayout} · {status.tableCapacity} seats</span></section>
        <button className="blackjack-primary" type="button" disabled={props.busy || (props.pending !== null && !retrying)} onClick={props.onJoin}>
          {props.busy ? 'Finding a seat…' : retrying ? 'Retry same request' : 'Join live table'}
        </button>
        <p>Choose your wager at the table before every round. Joining never moves your balance.</p>
      </main>
    )
  }
  if (session.kind === 'queue') {
    const seats = Array.from({ length: status.tableCapacity }, (_, index) => session.players[index] ?? null)
    return (
      <main className="blackjack-main blackjack-lobby">
        <section className="blackjack-title"><p>Queue position {session.position}</p><h1>Your seat is coming up</h1><span>Up to five people can join; the table starts with three occupied seats.</span></section>
        <div className="blackjack-queue-seats">
          {seats.map((seat, index) => <div className={seat ? 'is-filled' : ''} key={seat?.seatId ?? index}><strong>{seat?.displayName ?? 'Open seat'}</strong><small>Seat {(seat?.seat ?? index) + 1}</small></div>)}
        </div>
        <button className="blackjack-secondary" type="button" disabled={props.busy} onClick={() => props.onCancel(session)}>Leave queue</button>
      </main>
    )
  }
  return <TablePanel {...props} status={status} session={session} />
}

function TablePanel(props: ContentProps & { status: BlackjackTableStatus; session: BlackjackTablePlaySession }) {
  const { table } = props.session
  const current = table.seats.find((seat) => seat.isCurrentPlayer)
  const betting = table.phase === 'betting'
  const activeRound = table.phase === 'active'
  const insuranceRound = table.phase === 'insurance'
  const transition = table.transition !== null
  const seatsByNumber = new Map(table.seats.map((seat) => [seat.seat, seat]))
  const visualSeats = centeredSeatNumbers(props.status.tableCapacity, current?.seat)
  const dealerActive = table.transition?.startsWith('dealer-') ?? false
  return (
    <main className="blackjack-main blackjack-game">
      <section className="blackjack-table" aria-label="Live Blackjack table" data-phase={table.phase}>
        <div className="blackjack-table__round"><span>Round {Math.max(1, table.round)}</span><strong>{tableStatus(table, props.now)}</strong></div>
        <div className="blackjack-playfield">
          <div className={`blackjack-dealer${dealerActive ? ' is-active' : ''}`}>
            {betting ? <strong className="blackjack-dealer__idle">Dealer</strong> : <Hand label="Dealer" hand={table.dealer} scope="dealer" />}
          </div>
          <div className="blackjack-semicircle" aria-label="Player seats">
            {visualSeats.map((seatNumber, visualPosition) => {
              const seat = seatsByNumber.get(seatNumber)
              const active = seat?.seat === table.activeSeat
              const showTimer = Boolean(
                seat?.isCurrentPlayer
                && active
                && (activeRound || insuranceRound)
                && !transition
                && table.actionDeadlineAtUtc,
              )
              return (
                <div className={`blackjack-seat-slot blackjack-seat-slot--${visualPosition + 1}${seat?.isCurrentPlayer ? ' is-current-slot' : ''}`} key={seat?.seatId ?? seatNumber}>
                  {seat
                    ? <Seat seat={seat} active={active} showCards={!betting} timer={showTimer ? countdown(table.actionDeadlineAtUtc, props.now) : null} />
                    : <div className="blackjack-seat blackjack-seat--open"><strong>Open seat</strong><small>Joins next round</small></div>}
                </div>
              )
            })}
          </div>
        </div>
        <div className="blackjack-actions" aria-label="Blackjack controls">
          {betting && (
            <>
              <WagerInput status={props.status} wager={props.wager} busy={props.busy} onChange={props.onWagerChange} />
              <button className="blackjack-primary" type="button" disabled={props.busy || !validWager(props.wager, props.status)} onClick={() => props.onWager(props.session)}>
                {current?.wager ? `Update wager to R${props.wager.toFixed(2)}` : `Set wager · R${props.wager.toFixed(2)}`}
              </button>
            </>
          )}
          {insuranceRound && (['insurance', 'decline-insurance'] as const).map((action) => (
            <button type="button" key={action} disabled={props.busy || transition || !table.legalActions.includes(action)} onClick={() => props.onAction(props.session, action)}>
              {action === 'decline-insurance' ? 'No insurance' : 'Take insurance'}
            </button>
          ))}
          {activeRound && (['hit', 'stand', 'double', 'split', 'surrender'] as const).map((action) => (
            <button type="button" key={action} disabled={props.busy || transition || !table.legalActions.includes(action)} onClick={() => props.onAction(props.session, action)}>
              {formatLabel(action)}
            </button>
          ))}
          <button className="blackjack-leave" type="button" disabled={props.busy} onClick={() => props.onLeave(props.session)}>Leave table</button>
        </div>
      </section>
    </main>
  )
}

function Seat({ seat, active, showCards = true, timer = null }: { seat: BlackjackTableSeat; active: boolean; showCards?: boolean; timer?: string | null }) {
  const winning = seat.payout > seat.totalWager || seat.outcome === 'player-blackjack' || seat.outcome === 'player-win'
  const hands = playerHands(seat)
  const status = seatStatus(seat, hands)
  return (
    <article className={`blackjack-seat${seat.isCurrentPlayer ? ' is-current' : ''}${active ? ' is-active' : ''}${winning ? ' is-winner' : ''}`}>
      <div className="blackjack-seat__name">
        <strong>{seat.displayName}</strong>
        <small>{status}</small>
        {timer && <time className="blackjack-seat__timer" dateTime={`PT${timer}`}>{timer}</time>}
      </div>
      {showCards && <div className={`blackjack-seat__hands${hands.length > 1 ? ' has-split' : ''}`}>
        {hands.map((playerHand) => (
          <div className={playerHand.active ? 'is-active-hand' : ''} key={`${seat.seatId}-${playerHand.handNumber}`}>
            <Hand
              label={hands.length > 1 ? `Hand ${playerHand.handNumber}` : ''}
              hand={playerHand.hand}
              scope={`${seat.seatId}-${playerHand.handNumber}`}
              compact={!seat.isCurrentPlayer}
            />
          </div>
        ))}
      </div>}
      <div className="blackjack-seat__money">
        <span>R{seat.totalWager.toFixed(2)}</span>
        {(seat.insuranceWager ?? 0) > 0 && <span className="blackjack-seat__insurance">Insurance R{seat.insuranceWager?.toFixed(2)}</span>}
        {seat.payout > 0 && <strong>+R{seat.payout.toFixed(2)}</strong>}
      </div>
    </article>
  )
}

function playerHands(seat: BlackjackTableSeat): readonly BlackjackTablePlayerHand[] {
  if (seat.hands && seat.hands.length > 0) return seat.hands
  return [{
    handNumber: 1,
    hand: seat.hand,
    wager: seat.wager,
    totalWager: seat.totalWager,
    payout: seat.payout,
    status: seat.status,
    outcome: seat.outcome,
    lastAction: seat.lastAction,
    active: seat.status === 'playing',
  }]
}

function seatStatus(seat: BlackjackTableSeat, hands: readonly BlackjackTablePlayerHand[]): string {
  const activeHand = hands.find((hand) => hand.active)
  const value = activeHand?.lastAction ?? activeHand?.status ?? seat.outcome ?? seat.lastAction ?? seat.status
  return formatLabel(value)
}

function WagerInput({ status, wager, busy, onChange }: { status: BlackjackTableStatus; wager: number; busy: boolean; onChange: (value: number) => void }) {
  const bump = (direction: -1 | 1) => onChange(Math.min(status.maximumWager, Math.max(status.minimumWager, wager + direction * status.wagerIncrement)))
  return <div className="blackjack-wager" role="group" aria-label="Round wager"><button type="button" disabled={busy || wager <= status.minimumWager} onClick={() => bump(-1)}>−</button><label><span>Round wager</span><input inputMode="decimal" type="number" min={status.minimumWager} max={status.maximumWager} step={status.wagerIncrement} value={wager} disabled={busy} onChange={(event) => onChange(Number(event.target.value))} /></label><button type="button" disabled={busy || wager >= status.maximumWager} onClick={() => bump(1)}>+</button></div>
}

function Hand({ label, hand, scope, compact = false }: { label: string; hand: BlackjackTableHand; scope: string; compact?: boolean }) {
  return (
    <div className={`blackjack-hand${compact ? ' blackjack-hand--compact' : ''}`}>
      {(label || hand.score !== null) && <div className="blackjack-hand__heading">{label && <h2>{label}</h2>}{hand.score !== null && <span aria-label={`${label || 'Hand'} total ${hand.score}`}>{hand.score}</span>}</div>}
      <div className="blackjack-hand__cards">
        {hand.cards.length === 0 ? <span className="blackjack-hand__empty">Waiting</span> : hand.cards.map((card, index) => {
          const playingCard = toBlackjackTablePlayingCard(card, index, scope)
          return <span className="ff-card-slot blackjack-card-enter" key={`${scope}-${index}`} style={{ animationDelay: `${index * 90}ms` }}><PlayingCard card={playingCard ?? { id: `${scope}-hidden-${index}`, suit: 'spades', rank: 1 }} faceDown={playingCard === null} /></span>
        })}
      </div>
    </div>
  )
}

function tableStatus(table: BlackjackTable, now: number): string {
  if (table.transition === 'dealer-reveal') return 'Dealer reveals the hole card…'
  if (table.transition === 'dealer-draw') return 'Dealer draws…'
  if (table.transition === 'dealer-settle') return 'Settling the round…'
  if (table.transition === 'action-settle') return 'Action accepted…'
  if (table.transition === 'turn-pause') return 'Next player is thinking…'
  if (table.phase === 'betting') return `Choose a wager · ${countdown(table.wagerDeadlineAtUtc, now)}`
  if (table.phase === 'dealer') return 'Dealer plays'
  if (table.phase === 'insurance') {
    const active = table.seats.find((seat) => seat.seat === table.activeSeat)
    return active?.isCurrentPlayer ? 'Choose insurance' : `${active?.displayName ?? 'Next player'} is considering insurance`
  }
  if (table.phase === 'active') {
    const active = table.seats.find((seat) => seat.seat === table.activeSeat)
    return active?.isCurrentPlayer ? `${active.displayName}'s turn` : `${active?.displayName ?? 'Next player'} is thinking`
  }
  return 'Round complete'
}

export function BlackjackTablePreview({ mode = 'active' }: { mode?: 'active' | 'betting' }) {
  const preview = previewTable()
  const seats = new Map(preview.seats.map((seat) => [seat.seat, seat]))
  const visualSeats = centeredSeatNumbers(5, preview.seats.find((seat) => seat.isCurrentPlayer)?.seat)
  return <div className="blackjack-page blackjack-preview"><section className="blackjack-table" data-phase={mode}><div className="blackjack-table__round"><span>Blackjack</span><strong>{mode === 'betting' ? 'Choose your next wager' : 'Blackjack pays 3:2'}</strong></div><div className="blackjack-playfield"><div className="blackjack-dealer"><Hand label="Dealer" hand={preview.dealer} scope="preview-dealer" /></div><div className="blackjack-semicircle">{visualSeats.map((seatNumber, visualPosition) => <div className={`blackjack-seat-slot blackjack-seat-slot--${visualPosition + 1}${seats.get(seatNumber)?.isCurrentPlayer ? ' is-current-slot' : ''}`} key={seatNumber}>{seats.has(seatNumber) ? <Seat seat={seats.get(seatNumber)!} active={mode === 'active' && seatNumber === 0} timer={mode === 'active' && seatNumber === 0 ? '48s' : null} /> : <div className="blackjack-seat blackjack-seat--open"><strong>Open seat</strong><small>Joins next round</small></div>}</div>)}</div></div>{mode === 'betting' && <div className="blackjack-actions"><WagerInput status={previewStatus} wager={10} busy={false} onChange={() => undefined} /><button className="blackjack-primary" type="button">Set wager · R10.00</button><button className="blackjack-leave" type="button">Leave table</button></div>}</section></div>
}

function centeredSeatNumbers(capacity: number, currentSeat?: number): number[] {
  const center = Math.floor(capacity / 2)
  const anchor = currentSeat ?? center
  return Array.from({ length: capacity }, (_, visualPosition) => (
    anchor + visualPosition - center + capacity
  ) % capacity)
}

const previewStatus: BlackjackTableStatus = { available: true, minimumWager: .5, maximumWager: 100, wagerIncrement: .5, minimumStartOccupancy: 3, tableCapacity: 5, humanGraceSeconds: 5, actionDeadlineSeconds: 60, dealerRule: 'Dealer stands on all 17s', blackjackPayout: '3:2', doubleAllowed: true, splitAllowed: false, insuranceAllowed: false }

function previewTable(): BlackjackTable {
  const hand = (cards: BlackjackTableHand['cards'], score: number, blackjack = false): BlackjackTableHand => ({ cards, score, soft: blackjack, blackjack, bust: false })
  return {
    tableId: 'preview', phase: 'active', round: 7,
    dealer: hand([{ rank: '9', suit: 'clubs', hidden: false }, { hidden: true }], 9),
    seats: [
      { seatId: 'you', displayName: 'Tian', seat: 0, status: 'blackjack', wager: 10, totalWager: 10, payout: 25, outcome: 'player-blackjack', hand: hand([{ rank: 'A', suit: 'spades', hidden: false }, { rank: 'K', suit: 'hearts', hidden: false }], 21, true), isCurrentPlayer: true },
      { seatId: 'mina', displayName: 'Mina', seat: 1, status: 'stood', wager: 5, totalWager: 5, payout: 0, hand: hand([{ rank: '10', suit: 'diamonds', hidden: false }, { rank: '7', suit: 'clubs', hidden: false }], 17), isCurrentPlayer: false },
      { seatId: 'leo', displayName: 'Leo', seat: 2, status: 'playing', wager: 5, totalWager: 5, payout: 0, hand: hand([{ rank: '8', suit: 'hearts', hidden: false }, { rank: '8', suit: 'spades', hidden: false }], 16), isCurrentPlayer: false },
    ], activeSeat: 0, legalActions: [], createdAtUtc: '', updatedAtUtc: '', actionDeadlineAtUtc: null, wagerDeadlineAtUtc: null, transition: null, nextTransitionAtUtc: null, remainingActionMilliseconds: 0, remainingWagerMilliseconds: 0, remainingTransitionMilliseconds: 0,
  }
}

function validWager(value: number, status: BlackjackTableStatus): boolean {
  if (!Number.isFinite(value) || value < status.minimumWager || value > status.maximumWager) return false
  const increments = (value - status.minimumWager) / status.wagerIncrement
  return Math.abs(increments - Math.round(increments)) < 0.000_001
}

function countdown(deadline: string | null, now: number): string {
  if (deadline === null) return 'Waiting'
  return `${Math.ceil(Math.max(0, Date.parse(deadline) - now) / 1_000)}s`
}

function formatLabel(value: string): string {
  return value.split('-').map((part) => `${part.slice(0, 1).toUpperCase()}${part.slice(1)}`).join(' ')
}

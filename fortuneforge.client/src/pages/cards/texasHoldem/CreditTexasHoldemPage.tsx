import { useCallback, useEffect, useMemo, useState } from 'react'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import '../../../games/cards/shared/playingCards.css'
import {
  cancelCreditHoldemQueue,
  creditHoldemRaiseTarget,
  CreditHoldemRequestError,
  dealNextCreditHoldemHand,
  getCreditHoldemSession,
  getCreditHoldemStatus,
  isUncertainCreditHoldemFailure,
  joinCreditHoldemQueue,
  leaveCreditHoldemTable,
  postCreditHoldemAction,
  stableCreditHoldemMutation,
  toCreditHoldemPlayingCard,
  type CreditHoldemAction,
  type CreditHoldemCard,
  type CreditHoldemMutationResponse,
  type CreditHoldemQueueSession,
  type CreditHoldemResultSession,
  type CreditHoldemSeat,
  type CreditHoldemSession,
  type CreditHoldemStatus,
  type CreditHoldemTable,
  type CreditHoldemTableRule,
  type PendingCreditHoldemMutation,
} from '../../../games/cards/texasHoldem/creditHoldemApi'
import { CardRoomNavigation } from '../CardRoomNavigation'
import './texasHoldem.css'

type Availability =
  | Readonly<{ kind: 'loading' }>
  | Readonly<{ kind: 'ready'; status: CreditHoldemStatus; session: CreditHoldemSession }>
  | Readonly<{ kind: 'disabled'; message: string }>
  | Readonly<{ kind: 'error'; message: string }>

export function CreditTexasHoldemPage({ account }: { account: AccountSummary }) {
  const [availability, setAvailability] = useState<Availability>({ kind: 'loading' })
  const [balanceCredits, setBalanceCredits] = useState(account.balances.slotsCredits)
  const [pending, setPending] = useState<PendingCreditHoldemMutation | null>(null)
  const [busy, setBusy] = useState(false)
  const [requestError, setRequestError] = useState<string | null>(null)
  const [selectedRuleId, setSelectedRuleId] = useState('standard')

  const load = useCallback(async (quiet = false, signal?: AbortSignal) => {
    if (!quiet) setAvailability({ kind: 'loading' })
    try {
      const [status, session] = await Promise.all([
        getCreditHoldemStatus(signal), getCreditHoldemSession(signal),
      ])
      setAvailability({ kind: 'ready', status, session })
      setPending(null)
      setRequestError(null)
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (error instanceof CreditHoldemRequestError && error.code === 'credit-texas-holdem-disabled') {
        setAvailability({ kind: 'disabled', message: error.message })
        return
      }
      if (!quiet) setAvailability({
        kind: 'error',
        message: 'Hold’em could not reach the live table. No action was accepted.',
      })
    }
  }, [])

  const refresh = useCallback(async () => {
    try {
      const session = await getCreditHoldemSession()
      setAvailability((current) => current.kind === 'ready' ? { ...current, session } : current)
      setPending(null)
    } catch (error) {
      if (error instanceof CreditHoldemRequestError && error.code === 'credit-texas-holdem-disabled') {
        setAvailability({ kind: 'disabled', message: error.message })
      }
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void load(false, controller.signal)
    return () => controller.abort()
  }, [load])

  const currentSession = availability.kind === 'ready' ? availability.session : null
  const table = currentSession?.kind === 'match' ? currentSession.table
    : currentSession?.kind === 'result' ? currentSession.finalTable : null
  const currentSeat = table?.seats.find((seat) => seat.isCurrentPlayer)
  const waitingForTable = table?.status === 'active' && table.activeSeat !== currentSeat?.seat

  useEffect(() => {
    if (!currentSession || busy) return
    const interval = window.setInterval(() => void refresh(), waitingForTable ? 950 : 4200)
    return () => window.clearInterval(interval)
  }, [busy, currentSession, refresh, waitingForTable])

  const mutate = useCallback(async (
    fingerprint: string,
    operation: (key: string) => Promise<CreditHoldemMutationResponse>,
  ) => {
    const mutation = stableCreditHoldemMutation(pending, fingerprint)
    setPending(mutation)
    setBusy(true)
    setRequestError(null)
    try {
      const response = await operation(mutation.idempotencyKey)
      setAvailability((current) => current.kind === 'ready'
        ? { ...current, session: response.session }
        : current)
      setBalanceCredits(response.balanceCredits)
      setPending(null)
    } catch (error) {
      if (error instanceof CreditHoldemRequestError && error.code === 'credit-holdem-state-conflict') {
        await refresh()
        setRequestError('The table advanced. Your view has been refreshed; try again.')
      } else {
        if (!isUncertainCreditHoldemFailure(error)) setPending(null)
        setRequestError(error instanceof Error ? error.message : 'The table request failed.')
      }
    } finally {
      setBusy(false)
    }
  }, [pending, refresh])

  const navigation = (
    <CardRoomNavigation
      playerName={account.playerName}
      balanceCredits={balanceCredits}
      onBalanceChange={setBalanceCredits}
    />
  )
  if (availability.kind === 'loading') return <div className="credit-holdem-page">{navigation}<StateCard title="Opening the table…" body="Connecting to the dealer." /></div>
  if (availability.kind === 'disabled') return <div className="credit-holdem-page">{navigation}<StateCard title="Credit Hold’em is coming soon" body={availability.message} /></div>
  if (availability.kind === 'error') return <div className="credit-holdem-page">{navigation}<StateCard title="Table unavailable" body={availability.message} retry={() => load()} /></div>

  const { status, session } = availability
  const tableRules = status.tableRules?.length ? status.tableRules : [legacyRule(status)]
  const selectedRule = tableRules.find((rule) => rule.id === selectedRuleId) ?? tableRules[0]
  return (
    <div className="credit-holdem-page">
      {navigation}
      {requestError && (
        <div className="credit-holdem-error" role="alert">
          <span>{requestError}</span>
          <button type="button" onClick={() => void refresh()}>Refresh table</button>
        </div>
      )}
      <main className="credit-holdem-main">
        {session.kind === 'idle' && (
          <section className="credit-holdem-lobby">
            <span className="credit-holdem-kicker">Choose a table · 3–5 seats</span>
            <h1>Texas Hold’em</h1>
            <p>Joining is free. Every table uses standard automatic blinds; choose the stakes and the most credits that can sit on the felt.</p>
            <div className="credit-holdem-rule-grid" role="radiogroup" aria-label="Table stakes">
              {tableRules.map((rule) => <label className={rule.id === selectedRule.id ? 'is-selected' : ''} key={rule.id}>
                <input type="radio" name="holdem-table" value={rule.id} checked={rule.id === selectedRule.id}
                  onChange={() => setSelectedRuleId(rule.id)} />
                <strong>{rule.name}</strong>
                <span>{rule.description}</span>
                <small>Table stack up to R{rule.maximumTableStackCredits.toFixed(2)}</small>
              </label>)}
            </div>
            <dl>
              <div><dt>Small blind</dt><dd>R{selectedRule.smallBlindCredits.toFixed(2)}</dd></div>
              <div><dt>Big blind</dt><dd>R{selectedRule.bigBlindCredits.toFixed(2)}</dd></div>
              <div><dt>Ante</dt><dd>{selectedRule.anteCredits > 0 ? `R${selectedRule.anteCredits.toFixed(2)}` : 'None'}</dd></div>
              <div><dt>Maximum at table</dt><dd>R{selectedRule.maximumTableStackCredits.toFixed(2)}</dd></div>
            </dl>
            <button
              className="credit-holdem-primary"
              type="button"
              disabled={busy || balanceCredits < selectedRule.bigBlindCredits}
              onClick={() => void mutate(`join:${selectedRule.id}:${session.version}`,
                (key) => joinCreditHoldemQueue(session.version, key, selectedRule.id))}
            >Join {selectedRule.name}</button>
            {balanceCredits < selectedRule.bigBlindCredits && <small>At least one big blind is needed to sit.</small>}
          </section>
        )}
        {session.kind === 'queue' && (
          <QueueView
            session={session}
            busy={busy}
            leave={() => void mutate(`leave-queue:${session.ticketId}:${session.version}`,
              (key) => cancelCreditHoldemQueue(session.ticketId, session.version, key))}
          />
        )}
        {session.kind === 'match' && (
          <TableView
            table={session.table}
            version={session.version}
            busy={busy}
            act={(action, raiseTo) => void mutate(
              `action:${session.table.matchId}:${session.version}:${action}:${raiseTo ?? ''}`,
              (key) => postCreditHoldemAction(
                session.table.matchId, action, session.version, key, raiseTo,
              ),
            )}
            leave={() => void mutate(`leave:${session.table.matchId}:${session.version}`,
              (key) => leaveCreditHoldemTable(session.table.matchId, session.version, key))}
          />
        )}
        {session.kind === 'result' && (
          <ResultView
            session={session}
            busy={busy}
            next={() => void mutate(`next:${session.matchId}:${session.version}`,
              (key) => dealNextCreditHoldemHand(session.matchId, session.version, key))}
            leave={() => void mutate(`leave:${session.matchId}:${session.version}`,
              (key) => leaveCreditHoldemTable(session.matchId, session.version, key))}
          />
        )}
      </main>
    </div>
  )
}

function QueueView({ session, busy, leave }: {
  session: CreditHoldemQueueSession; busy: boolean; leave: () => void
}) {
  return (
    <section className="credit-holdem-lobby">
      <span className="credit-holdem-kicker">Finding your table</span>
      <h1>Seat {session.position} in line</h1>
      <p>No credits are committed while you wait. Open seats are offered to real players first.</p>
      <div className="credit-holdem-queue-seats">
        {Array.from({ length: 5 }, (_, index) => {
          const seat = session.players[index]
          return <div className={seat ? 'is-filled' : ''} key={seat?.seatId ?? index}>
            <span>{seat ? initials(seat.displayName) : '+'}</span>
            <strong>{seat?.displayName ?? 'Open seat'}</strong>
          </div>
        })}
      </div>
      <button type="button" disabled={busy} onClick={leave}>Leave queue</button>
    </section>
  )
}

function ResultView({ session, busy, next, leave }: {
  session: CreditHoldemResultSession; busy: boolean; next: () => void; leave: () => void
}) {
  return (
    <section className="credit-holdem-result">
      <CreditHoldemTableSurface table={session.finalTable} revealDelay={130} />
      <div className="credit-holdem-result__controls">
        <div>
          <strong>Hand {session.handNumber} settled</strong>
          <small>Account payouts were applied by the server exactly once.</small>
        </div>
        <button className="credit-holdem-primary" type="button" disabled={busy} onClick={next}>Deal next hand</button>
        <button type="button" disabled={busy} onClick={leave}>Leave table</button>
      </div>
    </section>
  )
}

function TableView({ table, version, busy, act, leave }: {
  table: CreditHoldemTable
  version: number
  busy: boolean
  act: (action: CreditHoldemAction, raiseTo?: number) => void
  leave: () => void
}) {
  const viewer = table.seats.find((seat) => seat.isCurrentPlayer)
  const callAmount = viewer ? Math.max(0, table.currentBet - viewer.committedRound) : 0
  const defaultRaise = creditHoldemRaiseTarget(table)
  const raiseStep = Math.max(1, Math.round((table.tableRule?.bigBlindCredits ?? 1) * 100))
  const [raiseTo, setRaiseTo] = useState(defaultRaise)
  useEffect(() => setRaiseTo(defaultRaise), [defaultRaise, table.handNumber, table.street])
  const changeRaise = (value: number) => setRaiseTo(Math.min(
    table.maximumRaiseTo,
    Math.max(defaultRaise, Number.isFinite(value) ? Math.round(value) : defaultRaise),
  ))
  return (
    <section className="credit-holdem-match" data-version={version}>
      <CreditHoldemTableSurface table={table} revealDelay={260} />
      <div className="credit-holdem-actions">
        {table.legalActions.includes('fold') && <button type="button" disabled={busy} onClick={() => act('fold')}>Fold</button>}
        {table.legalActions.includes('check') && <button type="button" disabled={busy} onClick={() => act('check')}>Check</button>}
        {table.legalActions.includes('call') && <button type="button" disabled={busy} onClick={() => act('call')}>Call R{chips(callAmount)}</button>}
        {table.legalActions.includes('raise') && <div className="credit-holdem-raise-control">
          <label><span>Raise to</span><strong>R{chips(raiseTo)}</strong></label>
          <input type="range" min={defaultRaise} max={table.maximumRaiseTo} step={raiseStep} value={raiseTo}
            onChange={(event) => changeRaise(Number(event.currentTarget.value))} aria-label="Raise amount" />
          <div>
            <button type="button" disabled={busy || raiseTo <= defaultRaise}
              onClick={() => changeRaise(raiseTo - raiseStep)}>−</button>
            <button className="credit-holdem-primary" type="button" disabled={busy}
              onClick={() => act('raise', raiseTo)}>Raise R{chips(raiseTo)}</button>
            <button type="button" disabled={busy || raiseTo >= table.maximumRaiseTo}
              onClick={() => changeRaise(raiseTo + raiseStep)}>+</button>
          </div>
        </div>}
        <button className="credit-holdem-leave" type="button" disabled={busy} onClick={leave}>Leave after hand</button>
      </div>
    </section>
  )
}

export function CreditHoldemTableSurface({ table, revealDelay }: { table: CreditHoldemTable; revealDelay: number }) {
  const [visibleCards, setVisibleCards] = useState(table.communityCards.length)
  const handKey = `${table.matchId}:${table.handNumber}`
  useEffect(() => setVisibleCards(0), [handKey])
  useEffect(() => {
    if (visibleCards >= table.communityCards.length) return
    const timer = window.setTimeout(() => setVisibleCards((count) => count + 1), revealDelay)
    return () => window.clearTimeout(timer)
  }, [revealDelay, table.communityCards.length, visibleCards])

  const orderedSeats = useMemo(() => arrangeSeats(table.seats), [table.seats])
  return (
    <div className="credit-holdem-table" aria-label="Texas Hold’em table">
      <div className="credit-holdem-primary-info">
        <div><span>Pot</span><strong>R{chips(table.pot)}</strong></div>
        <div><span>Current bet</span><strong>R{chips(table.currentBet)}</strong></div>
      </div>
      <div className="credit-holdem-community" aria-label="Community cards">
        {Array.from({ length: 5 }, (_, index) => (
          <CardSlot card={index < visibleCards ? table.communityCards[index] : undefined}
            index={index} scope={`${table.matchId}-board`} key={index} />
        ))}
      </div>
      <div className="credit-holdem-seat-ring">
        {orderedSeats.map(({ seat, position }) => (
          <SeatView
            key={seat.seatId}
            seat={seat}
            position={position}
            dealer={seat.seat === table.dealerSeat}
            active={seat.seat === table.activeSeat && table.status === 'active'}
            winner={table.winningSeatIds.includes(seat.seatId)}
            winningAmount={table.winningSeatIds.includes(seat.seatId) ? table.winningAmount : 0}
          />
        ))}
      </div>
    </div>
  )
}

function SeatView({ seat, position, dealer, active, winner, winningAmount }: {
  seat: CreditHoldemSeat; position: number; dealer: boolean; active: boolean
  winner: boolean; winningAmount: number
}) {
  return (
    <article className={`credit-holdem-player seat-pos-${position}${seat.isCurrentPlayer ? ' is-current' : ''}${active ? ' is-active' : ''}${winner ? ' is-winner' : ''}`}>
      <div className="credit-holdem-player__name">
        <span>{initials(seat.displayName)}</span>
        <div><strong>{seat.displayName}</strong><small>R{chips(seat.stack)}</small></div>
        {dealer && <i title="Dealer">D</i>}
      </div>
      <div className="credit-holdem-seat-cards">
        {seat.holeCards.map((card, index) => <CardSlot card={card} index={index} scope={seat.seatId} key={index} />)}
      </div>
      <div className="credit-holdem-action-state">
        <strong>{seat.lastAction ?? (active ? 'Thinking…' : seat.status)}</strong>
        <span>Round R{chips(seat.committedRound)}</span>
      </div>
      {winner && <div className="credit-holdem-win">+R{chips(winningAmount)}</div>}
    </article>
  )
}

function CardSlot({ card, index, scope }: {
  card?: CreditHoldemCard; index: number; scope: string
}) {
  if (!card) return <span className="credit-holdem-card-empty" aria-hidden="true">♠</span>
  const playingCard = toCreditHoldemPlayingCard(card, index, scope)
  return <span className="ff-card-slot">{playingCard
    ? <PlayingCard card={playingCard} />
    : <PlayingCard card={{ id: `${scope}-hidden-${index}`, suit: 'spades', rank: 1 }} faceDown />}</span>
}

function arrangeSeats(seats: readonly CreditHoldemSeat[]): Array<{ seat: CreditHoldemSeat; position: number }> {
  const current = seats.find((seat) => seat.isCurrentPlayer) ?? seats[0]
  if (!current) return []
  const sorted = [...seats].sort((left, right) => left.seat - right.seat)
  const start = sorted.findIndex((seat) => seat.seatId === current.seatId)
  const rotated = [...sorted.slice(start), ...sorted.slice(0, start)]
  const maps: Record<number, number[]> = { 1: [0], 2: [0, 3], 3: [0, 2, 4], 4: [0, 1, 3, 4], 5: [0, 1, 2, 3, 4] }
  return rotated.map((seat, index) => ({ seat, position: maps[rotated.length]?.[index] ?? index }))
}

function StateCard({ title, body, retry }: { title: string; body: string; retry?: () => void }) {
  return <main className="credit-holdem-state"><span>♠</span><h1>{title}</h1><p>{body}</p>
    {retry && <button type="button" onClick={retry}>Try again</button>}</main>
}
function chips(value: number): string { return (value / 100).toFixed(2) }
function initials(value: string): string {
  return value.split(/[^a-z0-9]+/i).filter(Boolean).slice(0, 2).map((part) => part[0]).join('').toUpperCase() || 'P'
}

function legacyRule(status: CreditHoldemStatus): CreditHoldemTableRule {
  return {
    id: 'standard', name: 'Standard', description: 'Automatic blinds · no ante',
    smallBlindCredits: status.smallBlindCredits, bigBlindCredits: status.bigBlindCredits,
    anteCredits: 0, maximumTableStackCredits: 100,
  }
}

/** Deterministic actual-table fixture for the 3:2 library thumbnail capture. */
export function CreditTexasHoldemPreview() {
  return <div className="credit-holdem-page credit-holdem-preview"><main className="credit-holdem-main">
    <CreditHoldemTableSurface table={previewTable} revealDelay={0} />
  </main></div>
}

const previewCards = (cards: Array<[string, 'clubs' | 'diamonds' | 'hearts' | 'spades']>): CreditHoldemCard[] =>
  cards.map(([rank, suit]) => ({ rank, suit, hidden: false }))
const previewSeat = (seat: number, name: string, current = false): CreditHoldemSeat => ({
  seatId: `preview-${seat}`, displayName: name, seat, startingStack: 2500, stack: 2180,
  committed: 320, committedRound: seat === 0 ? 100 : 200, status: 'active',
  lastAction: seat === 0 ? 'call' : seat === 1 ? 'raise' : 'check',
  holeCards: current ? previewCards([['A', 'spades'], ['K', 'spades']]) : [{ hidden: true }, { hidden: true }],
  isCurrentPlayer: current,
})
const previewTable: CreditHoldemTable = {
  matchId: 'preview-table', status: 'active', street: 'turn', handNumber: 7, dealerSeat: 3,
  activeSeat: 0, pot: 940, currentBet: 200, minimumRaiseTo: 400, maximumRaiseTo: 2180,
  communityCards: previewCards([['A', 'hearts'], ['10', 'spades'], ['7', 'clubs'], ['Q', 'diamonds']]),
  seats: [previewSeat(0, 'RiverMoss', true), previewSeat(1, 'NightOwl84'), previewSeat(2, 'LuckyNova'), previewSeat(3, 'CardinalSky')],
  legalActions: ['fold', 'call', 'raise'], winningSeatIds: [], winningAmount: 0,
  startedAtUtc: '2026-08-16T00:00:00Z', matchDeadlineAtUtc: '2026-08-16T01:00:00Z',
  actionDeadlineAtUtc: '2026-08-16T00:00:25Z', remainingActionMilliseconds: 25000,
}

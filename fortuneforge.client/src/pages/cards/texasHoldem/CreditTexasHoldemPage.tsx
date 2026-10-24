import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type MouseEventHandler } from 'react'
import { HoldemActionBar, HoldemLobby, HoldemQueue, HoldemTableSurface } from '@fortuneforge/games-texas-holdem'
import '@fortuneforge/games-texas-holdem/styles.css'
import { InGameShell } from '../../../components/InGameShell'
import { getCurrentAccount, type AccountSummary } from '../../../features/account/services/accountsApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import '../../../games/cards/shared/playingCards.css'
import {
  cancelCreditHoldemQueue,
  creditHoldemJoinFingerprint,
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
  type CreditHoldemResultSession,
  type CreditHoldemSeat,
  type CreditHoldemSession,
  type CreditHoldemStatus,
  type CreditHoldemTable,
  type CreditHoldemTableRule,
  type PendingCreditHoldemMutation,
} from '../../../games/cards/texasHoldem/creditHoldemApi'
import { useCardAudioClick } from '../../../games/cards/shared/cardAudio'

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
  const [buyInAmount, setBuyInAmount] = useState<string | null>(null)
  const pendingRequest = useRef<{
    fingerprint: string; mutation: PendingCreditHoldemMutation
    operation: (key: string) => Promise<CreditHoldemMutationResponse>
  } | null>(null)
  const inFlight = useRef(false)
  const balanceVersion = useRef(0)
  const observedSession = useRef<CreditHoldemSession | null>(null)
  const [muted, setMuted] = useState(false)
  const cardAudioClick = useCardAudioClick()
  const onCardAudioClick: MouseEventHandler<HTMLDivElement> = useCallback(event => {
    if (!muted) cardAudioClick(event)
  }, [cardAudioClick, muted])

  const syncAccountBalance = useCallback(async () => {
    const version = ++balanceVersion.current
    try {
      const current = await getCurrentAccount()
      if (current.userId === account.userId && version === balanceVersion.current) setBalanceCredits(current.balances.slotsCredits)
    } catch { /* The table stays usable while a balance refresh is unavailable. */ }
  }, [account.userId])

  const load = useCallback(async (quiet = false, signal?: AbortSignal) => {
    if (!quiet) setAvailability({ kind: 'loading' })
    try {
      const [status, session] = await Promise.all([
        getCreditHoldemStatus(signal), getCreditHoldemSession(signal),
      ])
      setAvailability({ kind: 'ready', status, session })
      observedSession.current = session
      setPending(null)
      pendingRequest.current = null
      setRequestError(null)
      if (session.kind !== 'idle') void syncAccountBalance()
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
  }, [syncAccountBalance])

  const refresh = useCallback(async () => {
    try {
      const session = await getCreditHoldemSession()
      const previousSession = observedSession.current
      observedSession.current = session
      const returnedToLobby = previousSession !== null && previousSession.kind !== 'idle' && session.kind === 'idle'
      setAvailability((current) => current.kind === 'ready' ? { ...current, session } : current)
      const recoveringJoin = pendingRequest.current?.fingerprint.startsWith('join:') === true
      // An idle read cannot prove that an uncertain reservation was rejected.
      if (session.kind !== 'idle' || !recoveringJoin) {
        pendingRequest.current = null
        setPending(null)
        setRequestError(null)
        if (recoveringJoin || returnedToLobby) void syncAccountBalance()
      }
    } catch (error) {
      if (error instanceof CreditHoldemRequestError && error.code === 'credit-texas-holdem-disabled') {
        setAvailability({ kind: 'disabled', message: error.message })
      }
    }
  }, [syncAccountBalance])

  useEffect(() => {
    const controller = new AbortController()
    void load(false, controller.signal)
    return () => controller.abort()
  }, [load])

  const currentSession = availability.kind === 'ready' ? availability.session : null
  const table = currentSession?.kind === 'match' ? currentSession.table
    : currentSession?.kind === 'result' ? currentSession.finalTable : null
  const currentSeat = table?.seats.find((seat) => seat.isCurrentPlayer)
  const waitingForTable = currentSession?.kind === 'queue'
    || (table?.status === 'active' && table.activeSeat !== currentSeat?.seat)

  useEffect(() => {
    if (!currentSession || busy) return
    const interval = window.setInterval(() => void refresh(), waitingForTable ? 950 : 4200)
    return () => window.clearInterval(interval)
  }, [busy, currentSession, refresh, waitingForTable])

  const mutate = useCallback(async (
    fingerprint: string,
    operation: (key: string) => Promise<CreditHoldemMutationResponse>,
  ) => {
    if (inFlight.current) return
    inFlight.current = true
    balanceVersion.current++
    const previous = pendingRequest.current
    const mutation = previous?.fingerprint === fingerprint
      ? previous.mutation : stableCreditHoldemMutation(pending, fingerprint)
    const request = previous?.fingerprint === fingerprint ? previous : { fingerprint, mutation, operation }
    pendingRequest.current = request
    setPending(mutation)
    setBusy(true)
    setRequestError(null)
    try {
      const response = await request.operation(mutation.idempotencyKey)
      observedSession.current = response.session
      setAvailability((current) => current.kind === 'ready'
        ? { ...current, session: response.session }
        : current)
      setBalanceCredits(response.balanceCredits)
      balanceVersion.current++
      setPending(null)
      pendingRequest.current = null
    } catch (error) {
      if (error instanceof CreditHoldemRequestError && error.code === 'credit-holdem-state-conflict') {
        pendingRequest.current = null
        setPending(null)
        await refresh()
        setRequestError('The table advanced. Your view has been refreshed; try again.')
      } else {
        if (!isUncertainCreditHoldemFailure(error)) {
          setPending(null)
          pendingRequest.current = null
        }
        setRequestError(error instanceof Error ? error.message : 'The table request failed.')
      }
    } finally {
      setBusy(false)
      inFlight.current = false
    }
  }, [pending, refresh])

  const navbarAccount = { ...account, balances: { ...account.balances, slotsCredits: balanceCredits } }
  if (availability.kind === 'loading') return <InGameShell account={navbarAccount} title="Texas Hold’em" theme="cards" bodyClassName="ff-holdem-shell-body"><div className="ff-holdem-page" onClickCapture={onCardAudioClick}><StateCard title="Opening the table…" body="Connecting to the dealer." /></div></InGameShell>
  if (availability.kind === 'disabled') return <InGameShell account={navbarAccount} title="Texas Hold’em" theme="cards" bodyClassName="ff-holdem-shell-body"><div className="ff-holdem-page" onClickCapture={onCardAudioClick}><StateCard title="Credit Hold’em is coming soon" body={availability.message} /></div></InGameShell>
  if (availability.kind === 'error') return <InGameShell account={navbarAccount} title="Texas Hold’em" theme="cards" bodyClassName="ff-holdem-shell-body"><div className="ff-holdem-page" onClickCapture={onCardAudioClick}><StateCard title="Table unavailable" body={availability.message} retry={() => load()} /></div></InGameShell>

  const { status, session } = availability
  const tableRules = status.tableRules?.length ? status.tableRules : [legacyRule(status)]
  const selectedRule = tableRules.find((rule) => rule.id === selectedRuleId) ?? tableRules[0]
  const maximumBuyIn = Math.max(0, Math.floor(Math.min(balanceCredits, selectedRule.maximumTableStackCredits) * 100 + .000001) / 100)
  const displayedBuyIn = buyInAmount ?? (maximumBuyIn >= (selectedRule.minimumBuyInCredits ?? selectedRule.bigBlindCredits) ? maximumBuyIn.toFixed(2) : '')
  const findingTable = session.kind === 'idle' && pending?.fingerprint.startsWith('join:') === true
  return (
    <InGameShell account={navbarAccount} title="Texas Hold’em" theme="cards" bodyClassName="ff-holdem-shell-body">
      <div className="ff-holdem-page" onClickCapture={onCardAudioClick}>
      {requestError && (
        <div className="ff-holdem-error" role="alert">
          <span>{requestError}</span>
          {pending && <button type="button" disabled={busy} onClick={() => {
            const request = pendingRequest.current
            if (request) void mutate(request.fingerprint, request.operation)
          }}>Retry request</button>}
          <button type="button" disabled={busy} onClick={() => void refresh()}>Refresh table</button>
        </div>
      )}
      <main className="ff-holdem-main">
        <CreditHoldemGameHeader muted={muted} onToggleSound={() => setMuted(value => !value)}
          tableRules={tableRules} actionDeadlineSeconds={status.actionDeadlineSeconds} maximumSeats={status.maximumSeats}/>
        {session.kind === 'idle' && findingTable && <FindingTableView busy={busy} />}
        {session.kind === 'idle' && !findingTable && (
          <HoldemLobby rules={tableRules} selectedRuleId={selectedRule.id} balanceCredits={balanceCredits} busy={busy}
            buyInAmount={displayedBuyIn} onBuyInAmountChange={setBuyInAmount}
            onSelectRule={id => { setSelectedRuleId(id); setBuyInAmount(null) }}
            onJoin={(rule, amount) => void mutate(creditHoldemJoinFingerprint(rule.id, session.version, amount),
              key => joinCreditHoldemQueue(session.version, key, rule.id, amount))}/>
        )}
        {session.kind === 'queue' && (
          <FindingTableView
            busy={busy}
            cancel={() => void mutate(`leave-queue:${session.ticketId}:${session.version}`,
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
          <CreditHoldemResultView
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
    </InGameShell>
  )
}

export function CreditHoldemGameHeader({ muted, onToggleSound, tableRules = [], actionDeadlineSeconds = 30, maximumSeats = 5 }: {
  muted: boolean
  onToggleSound: () => void
  tableRules?: readonly CreditHoldemTableRule[]
  actionDeadlineSeconds?: number
  maximumSeats?: number
}) {
  const [rulesOpen, setRulesOpen] = useState(false)
  const dialog = useRef<HTMLDialogElement>(null)
  const rulesTrigger = useRef<HTMLButtonElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const titleId = useId()

  useEffect(() => {
    if (!rulesOpen) return
    const element = dialog.current
    const trigger = rulesTrigger.current
    if (!element) return
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    if (typeof element.showModal === 'function') element.showModal()
    else element.setAttribute('open', '')
    closeButton.current?.focus()
    return () => {
      if (typeof element.close === 'function' && element.open) element.close()
      document.body.style.overflow = previousOverflow
      trigger?.focus()
    }
  }, [rulesOpen])

  function onRulesKeyDown(event: KeyboardEvent<HTMLDialogElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      setRulesOpen(false)
      return
    }
    if (event.key !== 'Tab') return
    const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(
      'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
    ))
    const first = controls[0], last = controls[controls.length - 1]
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last?.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first?.focus()
    }
  }

  return <>
    <header className="ff-holdem-game-header">
      <h1>Texas Hold’em</h1>
      <div className="ff-holdem-game-tools">
        <button type="button" aria-label={muted ? 'Sound off' : 'Sound on'} aria-pressed={!muted}
          title={muted ? 'Turn sound on' : 'Turn sound off'} onClick={onToggleSound}>
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M11 5 6 9H3v6h3l5 4V5Z"/>
            {muted ? <path d="m16 9 5 6m0-6-5 6"/> : <><path d="M15 8a6 6 0 0 1 0 8"/><path d="M18 5a10 10 0 0 1 0 14"/></>}
          </svg>
        </button>
        <button ref={rulesTrigger} type="button" aria-label="How to play" aria-haspopup="dialog" aria-expanded={rulesOpen}
          onClick={() => setRulesOpen(true)}>?</button>
      </div>
    </header>
    {rulesOpen && <dialog ref={dialog} className="ff-holdem-rules" aria-labelledby={titleId} aria-modal="true"
      onCancel={event => { event.preventDefault(); setRulesOpen(false) }} onKeyDown={onRulesKeyDown}
      onClick={event => { if (event.target === event.currentTarget) setRulesOpen(false) }}>
      <div>
        <button ref={closeButton} type="button" className="ff-holdem-rules__close" aria-label="Close how to play" onClick={() => setRulesOpen(false)}>×</button>
        <p className="ff-holdem-rules__eyebrow">A seat at the table</p>
        <h2 id={titleId}>How to play Texas Hold’em</h2>
        <p>Make the best five-card poker hand using any combination of your two private cards and the five community cards. Up to {maximumSeats} players share the table. You play against the other hands, not against the dealer.</p>
        <h3>The deal</h3>
        <p>The dealer button moves each hand. The next two seats post the small and big blinds automatically. Each player receives two private cards, then betting takes place before the flop, after the three-card flop, after the turn and after the river.</p>
        <h3>Your turn</h3>
        <dl>
          <div><dt>Check</dt><dd>Stay in without adding chips when there is no extra bet to match.</dd></div>
          <div><dt>Call</dt><dd>Match the current bet, or go all-in for your remaining stack.</dd></div>
          <div><dt>Raise</dt><dd>Increase the total you have put into this betting round. The displayed amount is your total bet, not the extra chips added. This is no-limit poker: you may raise up to your available stack when the table allows it.</dd></div>
          <div><dt>Fold</dt><dd>Give up this hand. Chips already committed stay in the pot.</dd></div>
        </dl>
        <p>The table only enables legal actions on your turn. A short all-in can be smaller than the normal minimum raise, and does not always reopen raising for a player who has already acted.</p>
        {tableRules.length > 0 && <>
          <h3>Table stakes</h3>
          <div className="ff-holdem-rules__stakes"><table>
            <caption>Limits are specific to the table you join.</caption>
            <thead><tr><th scope="col">Table</th><th scope="col">Small / big blind</th><th scope="col">Ante</th><th scope="col">Minimum buy-in</th><th scope="col">Maximum buy-in</th></tr></thead>
            <tbody>{tableRules.map(rule => <tr key={rule.id}><th scope="row">{rule.name}</th><td>{credits(rule.smallBlindCredits)} / {credits(rule.bigBlindCredits)}</td><td>{rule.anteCredits > 0 ? credits(rule.anteCredits) : 'None'}</td><td>{credits(rule.minimumBuyInCredits ?? rule.bigBlindCredits)}</td><td>{credits(rule.maximumTableStackCredits)}</td></tr>)}</tbody>
          </table></div>
          <p>Choose your buy-in before joining. Moved from your account to table chips. Unspent chips return when you leave. Cancelling the queue returns your reserved buy-in. Your account balance and table chips are separate.</p>
          <p>Next hand keeps your current table chips; it does not buy more chips or refill from your account. Winnings can grow above the maximum buy-in. There is no separate fixed maximum bet: your remaining table chips and the legal-action controls determine how much you can commit.</p>
        </>}
        <h3>Winning the pot</h3>
        <p>Win when everyone else folds, or show the highest-ranked five-card hand at showdown. From highest to lowest: royal flush, straight flush, four of a kind, full house, flush, straight, three of a kind, two pair, one pair and high card. Equal hands split the pot. All-in players can win only the pots they contributed to.</p>
        <h3>The clock and leaving</h3>
        <p>You have {actionDeadlineSeconds} seconds to act. If time runs out, the table checks when no extra chips are due; otherwise it folds your hand. Opening these instructions does not pause the turn timer.</p>
        <p>Use Leave table to leave your seat and return your unspent table chips to your account. Leaving during an active hand folds it, even if you are all-in; committed chips remain in the pot. A confirmation appears before you leave an active hand.</p>
        <button type="button" className="ff-holdem-primary" onClick={() => setRulesOpen(false)}>Back to the table</button>
      </div>
    </dialog>}
  </>
}

function credits(value: number): string { return `R${value.toFixed(2)}` }

export function FindingTableView({ busy, cancel }: {
  busy: boolean
  cancel?: () => void
}) {
  return <HoldemQueue busy={busy} onCancel={cancel}/>
}

export function CreditHoldemResultView({ session, busy, next, leave }: {
  session: CreditHoldemResultSession; busy: boolean; next: () => void; leave: () => void
}) {
  const current = session.standings.find((standing) => standing.isCurrentPlayer)
  const wonHand = current !== undefined
    && session.finalTable.winningSeatIds.includes(current.seatId)
  // These are decimal Rand credits, scoped to the viewer; humanPayoutCredits
  // is the all-human aggregate, whereas table chips/pot amounts are cents.
  const collected = current?.payoutCredits ?? 0
  const handName = session.finalTable.seats.find((seat) => seat.isCurrentPlayer)?.handName
  const sharedPot = !wonHand && collected > 0
  const outcome = wonHand
    ? `R${collected.toFixed(2)} collected`
    : sharedPot
      ? `R${collected.toFixed(2)} returned`
      : handName ?? 'Hand complete'
  return (
    <section className="ff-holdem-result">
      <CreditHoldemTableSurface table={session.finalTable} revealDelay={130} />
      <div className={`ff-holdem-result__outcome${wonHand ? ' is-win' : ''}`} role="status">
        <strong>{outcome}</strong>
      </div>
      <div className="ff-holdem-result__controls">
        <button className="ff-holdem-primary" type="button" disabled={busy} onClick={next}>Next hand</button>
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
  return (
    <section className="ff-holdem-match" data-version={version}>
      <CreditHoldemTableSurface table={table} revealDelay={260} />
      <HoldemActionBar table={table} busy={busy} onAction={act} onLeave={leave}/>
      <details className="ff-holdem-hand-history"><summary>History</summary><ol>{table.actionLog?.map(entry => <li key={entry.sequence}><b>{entry.displayName}</b><span>{entry.action}{entry.amount > 0 ? ` · R${chips(entry.amount)}` : ''}</span></li>) ?? table.seats.filter(seat => seat.lastAction || seat.committed > 0).map(seat => <li key={seat.seatId}><b>{seat.displayName}</b><span>{seat.lastAction ?? seat.status} · R{chips(seat.committed)}</span></li>)}</ol></details>
    </section>
  )
}

export function CreditHoldemTableSurface({ table, revealDelay }: { table: CreditHoldemTable; revealDelay: number }) {
  return <HoldemTableSurface table={table} renderCard={renderHoldemCard} revealDelay={revealDelay}/>
}

function renderHoldemCard(card: CreditHoldemCard | undefined, index: number, scope: string) {
  if (!card) return null
  const playingCard = toCreditHoldemPlayingCard(card, index, scope)
  return <span className="ff-card-slot">{playingCard
    ? <PlayingCard card={playingCard} />
    : <PlayingCard card={{ id: `${scope}-hidden-${index}`, suit: 'spades', rank: 1 }} faceDown />}</span>
}

function StateCard({ title, body, retry }: { title: string; body: string; retry?: () => void }) {
  return <main className="ff-holdem-state"><span>♠</span><h1>{title}</h1><p>{body}</p>
    {retry && <button type="button" onClick={retry}>Try again</button>}</main>
}
function chips(value: number): string { return (value / 100).toFixed(2) }

function legacyRule(status: CreditHoldemStatus): CreditHoldemTableRule {
  return {
    id: 'standard', name: 'Standard', description: 'Automatic blinds · no ante',
    smallBlindCredits: status.smallBlindCredits, bigBlindCredits: status.bigBlindCredits,
    anteCredits: 0, maximumTableStackCredits: 100,
  }
}

/** Deterministic actual-table fixture for the 3:2 library thumbnail capture. */
export function CreditTexasHoldemPreview() {
  const [muted, setMuted] = useState(false)
  const cardAudioClick = useCardAudioClick()
  return <InGameShell title="Texas Hold’em" theme="cards" bodyClassName="ff-holdem-shell-body">
    <div className="ff-holdem-page ff-holdem-preview" onClickCapture={event => { if (!muted) cardAudioClick(event) }}><main className="ff-holdem-main">
      <CreditHoldemGameHeader muted={muted} onToggleSound={() => setMuted(value => !value)}/>
      <TableView table={previewTable} version={1} busy={false} act={() => undefined} leave={() => undefined} />
    </main></div>
  </InGameShell>
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
  actionDeadlineAtUtc: null, remainingActionMilliseconds: 25000,
}

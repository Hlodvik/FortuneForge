import { useCallback, useEffect, useId, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { InGameShell } from '../../../components/InGameShell'
import { GameLoadingScreen } from '../../../features/game-loading/GameLoadingScreen'
import { resolveGameLoadingEntry } from '../../../features/game-loading/gameLoadingRegistry'
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
import '@fontsource-variable/sora'
import '../../../games/cards/shared/playingCards.css'
import '../blackjack/blackjack.css'
import './threeCardPoker.css'
import chip1 from './chip-1.png'
import chip2 from './chip-2.png'
import chip5 from './chip-5.png'
import chip10 from './chip-10.png'
import chip25 from './chip-25.png'

type Availability = Readonly<{ kind: 'loading' }>
  | Readonly<{ kind: 'ready'; status: ThreeCardPokerStatus; session: ThreeCardPokerSession }>
  | Readonly<{ kind: 'error' | 'disabled'; message: string }>

export function ThreeCardPokerPage({ account }: { account: AccountSummary }) {
  const [availability, setAvailability] = useState<Availability>({ kind: 'loading' })
  const [balanceCredits, setBalanceCredits] = useState(account.balances.slotsCredits)
  const [ante, setAnte] = useState(0)
  const [pairPlus, setPairPlus] = useState(0)
  const [pending, setPending] = useState<PendingThreeCardPokerMutation | null>(null)
  const [busy, setBusy] = useState(false)
  const [requestError, setRequestError] = useState<string | null>(null)
  const refreshInFlightRef = useRef<Promise<void> | null>(null)
  const snapshotEpochRef = useRef(0)
  const latestSessionVersionRef = useRef(0)
  const onCardAudioClick = useCardAudioClick()

  const load = useCallback(async (signal?: AbortSignal) => {
    const epoch = ++snapshotEpochRef.current
    setAvailability({ kind: 'loading' })
    try {
      const [status, snapshot] = await Promise.all([getThreeCardPokerStatus(signal), getThreeCardPokerSnapshot(signal)])
      if (signal?.aborted || epoch !== snapshotEpochRef.current) return
      latestSessionVersionRef.current = snapshot.session.version
      setAnte((current) => current === 0 || validThreeCardPokerWager(current, status) ? current : 0)
      setPairPlus((current) => validThreeCardPokerWager(current, status, true) && status.pairPlusAllowed ? current : 0)
      setAvailability(status.available ? { kind: 'ready', status, session: snapshot.session } : { kind: 'disabled', message: 'The Three Card Poker table is currently unavailable.' })
      setBalanceCredits(snapshot.balanceCredits)
      setPending(null)
      setRequestError(null)
    } catch (error) {
      if (signal?.aborted || epoch !== snapshotEpochRef.current) return
      const disabled = error instanceof ThreeCardPokerRequestError && error.code === 'three-card-poker-table-disabled'
      setAvailability({ kind: disabled ? 'disabled' : 'error', message: disabled ? error.message : 'The Three Card Poker table could not be reached. Please retry.' })
    }
  }, [])

  const refreshSession = useCallback(() => {
    if (refreshInFlightRef.current) return refreshInFlightRef.current
    const epoch = snapshotEpochRef.current
    const refresh = (async () => {
      try {
        const snapshot = await getThreeCardPokerSnapshot()
        if (epoch !== snapshotEpochRef.current || snapshot.session.version < latestSessionVersionRef.current) return
        latestSessionVersionRef.current = snapshot.session.version
        setAvailability((current) => current.kind === 'ready' ? { ...current, session: snapshot.session } : current)
        setBalanceCredits(snapshot.balanceCredits)
        setPending(null)
        setRequestError(null)
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

  const runMutation = async (fingerprint: string, request: (key: string) => Promise<ThreeCardPokerMutationResponse>) => {
    if (pending && pending.fingerprint !== fingerprint) {
      setRequestError('Retry the same pending request or refresh the table before choosing another action.')
      return
    }
    const active = stableThreeCardPokerMutation(pending, fingerprint)
    ++snapshotEpochRef.current
    setPending(active)
    setRequestError(null)
    setBusy(true)
    try {
      const result = await request(active.idempotencyKey)
      ++snapshotEpochRef.current
      latestSessionVersionRef.current = result.session.version
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
  return <InGameShell account={navbarAccount} title="Three Card Poker" theme="cards" className="blackjack-game-shell three-card-poker-shell" bodyClassName="blackjack-shell-body"
    actions={availability.kind === 'ready' ? <ThreeCardPokerHowToPlay status={availability.status} /> : undefined}>
    <div className="blackjack-page blackjack-table-page three-card-poker-page" onClickCapture={onCardAudioClick}>
      {requestError && <div className="blackjack-error" role="alert"><span>{requestError}</span><button type="button" disabled={busy} onClick={() => { setPending(null); void load() }}>Refresh table</button></div>}
      <ThreeCardPokerContent availability={availability} balanceCredits={balanceCredits} ante={ante} pairPlus={pairPlus} busy={busy} pending={pending}
        onAnteChange={setAnte} onPairPlusChange={setPairPlus} onJoin={join} onCancel={cancel} onWager={wager} onSitOut={sitOut} onAction={act} onLeave={leave} onRefresh={() => void load()} />
    </div>
  </InGameShell>
}

type ContentProps = Readonly<{
  availability: Availability; balanceCredits: number; ante: number; pairPlus: number
  busy: boolean; pending: PendingThreeCardPokerMutation | null
  onAnteChange: (value: number) => void; onPairPlusChange: (value: number) => void
  onJoin: () => void; onCancel: (session: ThreeCardPokerQueueSession) => void
  onWager: (session: ThreeCardPokerPlaySession) => void; onSitOut: (session: ThreeCardPokerPlaySession) => void
  onAction: (session: ThreeCardPokerPlaySession, action: ThreeCardPokerAction) => void
  onLeave: (session: ThreeCardPokerPlaySession) => void; onRefresh: () => void
}>
export function ThreeCardPokerContent(props: ContentProps) {
  const { availability } = props
  if (availability.kind === 'loading') return <GameLoadingScreen entry={resolveGameLoadingEntry('/cards/three-card-poker')} statusLabel="Loading" />
  if (availability.kind !== 'ready') return <main className="blackjack-main blackjack-state" role={availability.kind === 'error' ? 'alert' : 'status'}><h1>{availability.kind === 'error' ? 'The table is offline.' : 'Three Card Poker is unavailable.'}</h1><p>{availability.message}</p><button type="button" onClick={props.onRefresh}>Try again</button></main>
  const { session, status } = availability
  if (session.kind === 'idle') {
    const retry = props.pending?.fingerprint === `join:${session.version}`
    return <main className="blackjack-main blackjack-lobby"><section className="blackjack-title"><h1>Three Card Poker</h1></section><button className="blackjack-primary" type="button" disabled={props.busy || (props.pending !== null && !retry)} onClick={props.onJoin}>{retry ? 'Retry same request' : 'Play'}</button></main>
  }
  if (session.kind === 'queue') return <OpeningTable><button className="blackjack-secondary" type="button" disabled={props.busy} onClick={() => props.onCancel(session)}>Cancel</button></OpeningTable>
  return <TablePanel {...props} status={status} session={session} />
}
function OpeningTable({ children }: { children?: ReactNode }) {
  return <main className="blackjack-main blackjack-lobby blackjack-finding" role="status"><span className="blackjack-finding__spinner" aria-hidden="true" /><section className="blackjack-title"><h1>Opening table…</h1></section>{children}</main>
}
const chipValues = [1, 2, 5, 10, 25] as const
type ChipValue = typeof chipValues[number]
type ChipPlacement = Readonly<{ spot: 'ante' | 'pair-plus'; value: ChipValue }>
const money = (value: number) => `R${value.toFixed(2)}`
const chipArt: Record<ChipValue, string> = { 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25 }
const cents = (value: number) => Math.round(value * 100)
function chipsForAmount(amount: number): ChipValue[] {
  const target = Math.round(cents(amount) / 100)
  if (target * 100 !== cents(amount)) return []
  if (target <= 0) return []
  const counts = new Array<number>(target + 1).fill(Number.POSITIVE_INFINITY)
  const previous = new Array<ChipValue | null>(target + 1).fill(null)
  counts[0] = 0
  for (let total = 1; total <= target; total++) {
    for (const value of chipValues) {
      const units = value
      if (units <= total && counts[total - units] + 1 < counts[total]) {
        counts[total] = counts[total - units] + 1
        previous[total] = value
      }
    }
  }
  const chips: ChipValue[] = []
  let remaining = target
  while (remaining > 0 && chips.length < 24) {
    const value = previous[remaining]
    if (value === null) break
    chips.push(value)
    remaining -= value
  }
  return chips
}
function TablePanel(props: ContentProps & { status: ThreeCardPokerStatus; session: ThreeCardPokerPlaySession }) {
  const { table } = props.session
  const current = table.seats.find((seat) => seat.isCurrentPlayer)
  const betting = table.phase === 'betting'
  const wagerTurn = betting && table.transition === 'human-wager' && table.activeSeat === current?.seat
  const actionTurn = table.phase === 'active' && table.transition === null && table.activeSeat === current?.seat
  const [placements, setPlacements] = useState<ChipPlacement[]>([])
  const [playPlacements, setPlayPlacements] = useState<ChipValue[]>([])
  const [selectedChip, setSelectedChip] = useState<ChipValue>(1)
  const [chipPickerOpen, setChipPickerOpen] = useState(false)
  const [visibleResult, setVisibleResult] = useState<{ roundKey: string; seat: ThreeCardPokerSeat } | null>(null)
  const shownResultKey = useRef<string | null>(null)
  const dismissedResultKey = useRef<string | null>(null)
  const decisionRef = useRef<HTMLDivElement>(null)
  const { onAnteChange, onPairPlusChange } = props
  useEffect(() => {
    setPlacements([])
    setPlayPlacements([])
    setChipPickerOpen(false)
    onAnteChange(0)
    onPairPlusChange(0)
  }, [table.tableId, table.round, onAnteChange, onPairPlusChange])
  const resultKey = `${table.tableId}:${table.round}`
  useEffect(() => {
    if (table.phase !== 'settled' || !current?.outcome || shownResultKey.current === resultKey || dismissedResultKey.current === resultKey) return
    shownResultKey.current = resultKey
    setVisibleResult({ roundKey: resultKey, seat: current })
  }, [table.phase, current, resultKey])
  useEffect(() => {
    if (actionTurn) decisionRef.current?.focus()
  }, [actionTurn, table.round])
  const dismissResult = () => {
    dismissedResultKey.current = visibleResult?.roundKey ?? resultKey
    setVisibleResult(null)
  }
  const placeChip = (spot: ChipPlacement['spot']) => {
    if (!wagerTurn || props.busy || props.pending) return
    const nextAnte = props.ante + (spot === 'ante' ? selectedChip : 0)
    const nextPairPlus = props.pairPlus + (spot === 'pair-plus' ? selectedChip : 0)
    if (cents(spot === 'ante' ? nextAnte : nextPairPlus) > cents(props.status.maximumWager) || cents(2 * nextAnte + nextPairPlus) > cents(props.balanceCredits)) return
    setPlacements((currentPlacements) => [...currentPlacements, { spot, value: selectedChip }])
    onAnteChange(cents(nextAnte) / 100)
    onPairPlusChange(cents(nextPairPlus) / 100)
  }
  const undoChip = () => {
    if (!wagerTurn || props.busy || props.pending || placements.length === 0) return
    const next = placements.slice(0, -1)
    setPlacements(next)
    onAnteChange(cents(next.filter((chip) => chip.spot === 'ante').reduce((sum, chip) => sum + chip.value, 0)) / 100)
    onPairPlusChange(cents(next.filter((chip) => chip.spot === 'pair-plus').reduce((sum, chip) => sum + chip.value, 0)) / 100)
  }
  const placedAnte = placements.filter((chip) => chip.spot === 'ante').map((chip) => chip.value)
  const placedPairPlus = placements.filter((chip) => chip.spot === 'pair-plus').map((chip) => chip.value)
  const playTarget = current?.ante ?? 0
  const playAmount = playPlacements.reduce((sum, value) => sum + value, 0)
  const canFundPlay = cents(playTarget) <= cents(props.balanceCredits)
  const canPlay = actionTurn && table.legalActions.includes('play') && canFundPlay
  const canAddPlayChip = canPlay && !props.busy && props.pending === null && cents(playAmount + selectedChip) <= cents(playTarget)
  const addPlayChip = () => {
    if (canAddPlayChip) setPlayPlacements((chips) => [...chips, selectedChip])
  }
  const undoPlayChip = () => {
    if (actionTurn && !props.busy && !props.pending) setPlayPlacements((chips) => chips.slice(0, -1))
  }
  const matchPlayWager = () => {
    if (canPlay && !props.busy && !props.pending) setPlayPlacements(chipsForAmount(playTarget))
  }
  const shownAnte = wagerTurn ? props.ante : current?.ante ?? 0
  const shownPairPlus = wagerTurn ? props.pairPlus : current?.pairPlus ?? 0
  const anteChips = cents(placedAnte.reduce((sum, value) => sum + value, 0)) === cents(shownAnte) ? placedAnte : chipsForAmount(shownAnte)
  const pairPlusChips = cents(placedPairPlus.reduce((sum, value) => sum + value, 0)) === cents(shownPairPlus) ? placedPairPlus : chipsForAmount(shownPairPlus)
  const count = table.dealer.cards.length + (current?.hand.cards.length ?? 0)
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
  const resultSeat = visibleResult?.seat ?? (table.phase === 'settled' && dismissedResultKey.current !== resultKey ? current : null)
  return <main className="blackjack-main blackjack-game"><section className="blackjack-table" aria-label="Three Card Poker table" data-phase={table.phase}>
    <p className="three-card-poker-sr-only" role="status">{actionTurn ? `Your turn. Add chips to Wager until it matches your ${money(playTarget)} Ante, then Play or Fold.` : wagerTurn ? 'Place your Ante and optional Pair Plus, then Deal.' : ''}</p>
    <div className="blackjack-playfield">
      <div className="three-card-poker-limits">MIN BET {money(props.status.minimumWager)} <span>•</span> MAX BET {money(props.status.maximumWager)}</div>
      <div className={`blackjack-dealer${table.phase === 'dealer' ? ' is-active' : ''}`}><Hand label="Dealer" hand={betting ? emptyHand : table.dealer} scope="dealer" /></div>
      <div className="three-card-poker-felt" aria-hidden="true"><strong>3 Card Poker</strong><span>Dealer plays with Queen or better</span></div>
      <div className="three-card-poker-player" role="group" aria-label="Your hand">
        {current && <Seat seat={current} betting={betting} active={current.seat === table.activeSeat} />}
      </div>
      <div className="three-card-poker-bets" aria-label="Betting spots">
        {props.status.pairPlusAllowed && <BetSpot kind="pair-plus" label="Pair Plus" amount={shownPairPlus} chips={pairPlusChips} onPlace={wagerTurn ? () => placeChip('pair-plus') : undefined} disabled={!wagerTurn || props.busy || props.pending !== null || cents(shownPairPlus + selectedChip) > cents(props.status.maximumWager) || cents(2 * props.ante + props.pairPlus + selectedChip) > cents(props.balanceCredits)} />}
        <BetSpot kind="ante" label="Ante" amount={shownAnte} chips={anteChips} onPlace={wagerTurn ? () => placeChip('ante') : undefined} disabled={!wagerTurn || props.busy || props.pending !== null || cents(shownAnte + selectedChip) > cents(props.status.maximumWager) || cents(2 * (props.ante + selectedChip) + props.pairPlus) > cents(props.balanceCredits)} />
        <BetSpot kind="play" label="Wager" amount={actionTurn ? playAmount : current?.playWager ?? 0} chips={actionTurn ? playPlacements : chipsForAmount(current?.playWager ?? 0)} onPlace={actionTurn ? addPlayChip : undefined} actionLabel={`Add ${money(selectedChip)} chip to Wager, ${money(playAmount)} of ${money(playTarget)}`} disabled={!canAddPlayChip} />
      </div>
    </div>
    {resultSeat?.outcome && <><button className="three-card-poker-result-backdrop" type="button" aria-label="Dismiss round result" onClick={dismissResult} /><RoundResult seat={resultSeat} onDismiss={dismissResult} /></>}
    <div className="blackjack-actions" aria-label="Three Card Poker controls">
      <div className="three-card-poker-action-stats"><span>Bet <strong>{money(wagerTurn ? props.ante + props.pairPlus : (current?.totalWager ?? 0) + (actionTurn ? playAmount : 0))}</strong></span><span>Balance <strong>{money(props.balanceCredits)}</strong></span><span>Win <strong>{money(table.phase === 'settled' ? Math.max(0, (current?.payout ?? 0) - (current?.totalWager ?? 0)) : 0)}</strong></span></div>
      {(wagerTurn || actionTurn) && <ChipPicker selectedChip={selectedChip} chipPickerOpen={chipPickerOpen} locked={props.busy || props.pending !== null} maximumChip={wagerTurn ? Math.min(props.status.maximumWager, props.balanceCredits) : Math.min(playTarget, props.balanceCredits)} onSelectChip={(value) => { setSelectedChip(value); setChipPickerOpen(false) }} onToggleChipPicker={() => setChipPickerOpen((open) => !open)} onCloseChipPicker={() => setChipPickerOpen(false)} />}
      {wagerTurn && <WagerInput status={props.status} balance={props.balanceCredits} ante={props.ante} pairPlus={props.pairPlus} locked={props.busy || props.pending !== null}
        canSubmit={!blocked(`wager:${table.tableId}:${props.ante}:${props.pairPlus}:${props.session.version}`)} canUndo={placements.length > 0} onUndo={undoChip} onSubmit={() => props.onWager(props.session)} />}
      {table.phase === 'active' && <div className="three-card-poker-decision-actions" ref={decisionRef} role="group" aria-label="Play or Fold" tabIndex={actionTurn ? -1 : undefined}>
        {actionTurn && <>
          <button className="three-card-poker-action-orb three-card-poker-action-orb--undo" type="button" aria-label="Undo wager chip" disabled={props.busy || props.pending !== null || playPlacements.length === 0} onClick={undoPlayChip}><UndoIcon /></button>
          <button className="three-card-poker-match-ante" type="button" disabled={!canPlay || props.busy || props.pending !== null} onClick={matchPlayWager}>Match Ante</button>
        </>}
        <button className="three-card-poker-action-orb three-card-poker-action-orb--fold" type="button" aria-label="Fold" disabled={!actionTurn || !table.legalActions.includes('fold') || blocked(`action:${table.tableId}:fold:${props.session.version}`)} onClick={() => props.onAction(props.session, 'fold')}><CloseIcon /></button>
        {actionTurn && <button className="three-card-poker-action-orb three-card-poker-action-orb--deal" type="button" aria-label={`Play ${money(playTarget)}`} disabled={!canPlay || cents(playAmount) !== cents(playTarget) || blocked(`action:${table.tableId}:play:${props.session.version}`)} onClick={() => props.onAction(props.session, 'play')}><CardStackIcon /></button>}
      </div>}
    </div>
    <button className="blackjack-leave blackjack-leave--corner" type="button" disabled={blocked(`leave:${table.tableId}:${props.session.version}`)} onClick={() => props.onLeave(props.session)}>Leave table</button>
  </section></main>
}
function Seat({ seat, betting, active }: { seat: ThreeCardPokerSeat; betting: boolean; active: boolean }) {
  const net = seat.payout - seat.totalWager
  return <article className={`blackjack-seat is-current${active ? ' is-active' : ''}${!betting && seat.outcome && net > 0 ? ' is-winner' : ''}`} aria-label="Your cards">
    <div className="blackjack-seat__hands"><div><Hand label="" hand={betting ? emptyHand : seat.hand} scope={seat.seatId} /></div></div>
    {!betting && seat.outcome && <div className="blackjack-seat__money"><strong className={net >= 0 ? 'is-positive' : 'is-negative'}>{net >= 0 ? '+' : '−'}R{Math.abs(net).toFixed(2)}</strong></div>}
  </article>
}
const emptyHand: ThreeCardPokerHand = { cards: [], rank: null }
function Hand({ label, hand, scope, compact = false }: { label: string; hand: ThreeCardPokerHand; scope: string; compact?: boolean }) {
  return <div className={`blackjack-hand${compact ? ' blackjack-hand--compact' : ''}`} style={{ '--blackjack-fan-width': 1.6 + (compact ? .4 : .75) * Math.max(0, hand.cards.length - 1) } as CSSProperties}>
    {(label || hand.rank) && <div className="blackjack-hand__heading">{label && <h2>{label}</h2>}{hand.rank && <small>{formatLabel(hand.rank)}</small>}</div>}
    <div className="blackjack-hand__cards">{hand.cards.length === 0 && [0, 1, 2].map((index) => <span className="three-card-poker-card-outline" key={`${scope}-outline-${index}`} aria-hidden="true" />)}{hand.cards.map((card, index) => {
      const playingCard = toThreeCardPokerPlayingCard(card, index, scope)
      return <span className="ff-card-slot blackjack-card-enter" key={`${scope}-${index}`}><PlayingCard card={playingCard ?? { id: `${scope}-hidden-${index}`, suit: 'spades', rank: 1 }} faceDown={playingCard === null} /></span>
    })}</div>
  </div>
}
function BetSpot({ kind, label, amount, chips, onPlace, actionLabel, disabled = false }: {
  kind: 'ante' | 'pair-plus' | 'play'; label: string; amount: number; chips: readonly ChipValue[]; onPlace?: () => void; actionLabel?: string; disabled?: boolean
}) {
  const visibleChips = chips.slice(-4)
  const contents = <><span className="three-card-poker-bet-spot__label">{label}</span><span className="three-card-poker-chip-stack" aria-hidden="true">{visibleChips.map((value, index) => <img className="three-card-poker-chip" src={chipArt[value]} alt="" data-value={value} style={{ '--chip-index': index } as CSSProperties} key={`${kind}-${index}-${value}`} />)}</span><strong className="three-card-poker-bet-spot__amount">{money(amount)}</strong></>
  const className = `three-card-poker-bet-spot three-card-poker-bet-spot--${kind}`
  if (onPlace) return <button className={className} type="button" aria-label={actionLabel ?? `Place chip on ${label}, ${money(amount)} placed`} disabled={disabled} onClick={onPlace} data-active="true" data-empty={chips.length === 0}>{contents}</button>
  return <div className={className} aria-label={kind === 'play' ? 'Wager spot' : `${label} wager`} data-empty={chips.length === 0}>{contents}</div>
}
function RoundResult({ seat, onDismiss }: { seat: ThreeCardPokerSeat; onDismiss: () => void }) {
  const net = cents(seat.payout - seat.totalWager) / 100
  const mainPayout = cents(seat.payout - seat.anteBonusPayout - seat.pairPlusPayout) / 100
  const antePayout = seat.outcome === 'player-win' || seat.outcome === 'dealer-not-qualified' ? seat.ante * 2 : seat.outcome === 'push' ? seat.ante : 0
  const playPayout = Math.max(0, cents(mainPayout - antePayout) / 100)
  const title = net > 0 ? 'Win' : net < 0 ? 'Loss' : 'Push'
  return <div className={`three-card-poker-result three-card-poker-result--${title.toLowerCase()}`} role="status" aria-label="Round result">
    <button className="three-card-poker-result__close" type="button" aria-label="Close round result" onClick={onDismiss}>×</button>
    <strong className="three-card-poker-result__title">{title}</strong>
    <dl><div><dt>Ante</dt><dd>{money(antePayout)}</dd></div><div><dt>Play</dt><dd>{money(playPayout)}</dd></div><div><dt>Ante Bonus</dt><dd>{money(seat.anteBonusPayout)}</dd></div><div><dt>Pair Plus</dt><dd>{money(seat.pairPlusPayout)}</dd></div></dl>
    <strong className="three-card-poker-result__net">{net >= 0 ? '+' : '−'}{money(Math.abs(net))}</strong>
  </div>
}
function CardStackIcon() {
  return <svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><rect x="12" y="11" width="38" height="43" rx="4" fill="#123d29" stroke="#edf2e4" strokeWidth="3" transform="rotate(-11 31 32)"/><rect x="17" y="11" width="38" height="43" rx="4" fill="#f4f5eb" stroke="#52715b" strokeWidth="2.5"/><path d="M36 18l12 15-12 15-12-15z" fill="#879b7c"/><path d="M36 24l7 9-7 9-7-9z" fill="#edf1e2"/></svg>
}
function CloseIcon() {
  return <svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><path d="M21 21 43 43M43 21 21 43" fill="none" stroke="currentColor" strokeWidth="7" strokeLinecap="round" /></svg>
}
function UndoIcon() {
  return <svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><path d="M23 25h19a13 13 0 1 1-12 18" fill="none" stroke="currentColor" strokeWidth="6" strokeLinecap="round"/><path d="m25 16-10 9 10 9" fill="none" stroke="currentColor" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round"/></svg>
}
function WagerInput({ status, balance, ante, pairPlus, locked, canSubmit, canUndo, onUndo, onSubmit }: {
  status: ThreeCardPokerStatus; balance: number; ante: number; pairPlus: number; locked: boolean; canSubmit: boolean
  canUndo: boolean; onUndo: () => void; onSubmit: () => void
}) {
  const valid = validThreeCardPokerWager(ante, status) && validThreeCardPokerWager(pairPlus, status, true) && cents(2 * ante + pairPlus) <= cents(balance)
  return <div className="three-card-poker-wager-wrap">
    <button className="three-card-poker-action-orb three-card-poker-action-orb--undo" type="button" aria-label="Undo chip" disabled={locked || !canUndo} onClick={onUndo}><UndoIcon /></button>
    <button className="three-card-poker-action-orb three-card-poker-action-orb--deal" type="button" aria-label="Deal" disabled={!canSubmit || !valid} onClick={onSubmit}><CardStackIcon /></button>
  </div>
}
function ChipPicker({ selectedChip, chipPickerOpen, locked, maximumChip, onSelectChip, onToggleChipPicker, onCloseChipPicker }: {
  selectedChip: ChipValue; chipPickerOpen: boolean; locked: boolean; maximumChip: number
  onSelectChip: (value: ChipValue) => void; onToggleChipPicker: () => void; onCloseChipPicker: () => void
}) {
  const picker = useRef<HTMLDivElement>(null)
  const selectedButton = useRef<HTMLButtonElement>(null)
  const optionsId = useId()
  const nextHigher = chipValues.find((value) => value > selectedChip && value <= maximumChip)
  const nextLower = [...chipValues].reverse().find((value) => value < selectedChip && value <= maximumChip)
  useEffect(() => {
    if (!chipPickerOpen) return
    const onEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { onCloseChipPicker(); selectedButton.current?.focus() }
    }
    const onOutside = (event: PointerEvent) => {
      if (!picker.current?.contains(event.target as Node)) onCloseChipPicker()
    }
    document.addEventListener('keydown', onEscape)
    document.addEventListener('pointerdown', onOutside)
    return () => { document.removeEventListener('keydown', onEscape); document.removeEventListener('pointerdown', onOutside) }
  }, [chipPickerOpen, onCloseChipPicker])
  return <div className="three-card-poker-chip-picker" role="group" aria-label="Chips" ref={picker}>
    <button className="three-card-poker-chip-picker__step is-up" type="button" aria-label="Increase chip value" disabled={locked || nextHigher === undefined} onClick={() => { if (nextHigher !== undefined) onSelectChip(nextHigher) }}><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 15 7-7 7 7" /></svg></button>
    <button className="three-card-poker-chip-picker__selected" type="button" ref={selectedButton} aria-label={`Choose chip value, currently ${money(selectedChip)}`} aria-expanded={chipPickerOpen} aria-controls={optionsId} disabled={locked} onClick={onToggleChipPicker}><img src={chipArt[selectedChip]} alt="" /></button>
    <button className="three-card-poker-chip-picker__step is-down" type="button" aria-label="Decrease chip value" disabled={locked || nextLower === undefined} onClick={() => { if (nextLower !== undefined) onSelectChip(nextLower) }}><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 9 7 7 7-7" /></svg></button>
    {chipPickerOpen && <div className="three-card-poker-chip-picker__options" id={optionsId} role="group" aria-label="Choose chip value">{chipValues.map((value) => <button className={`three-card-poker-chip-option${selectedChip === value ? ' is-selected' : ''}`} data-value={value} type="button" aria-label={`Select ${money(value)} chip`} aria-pressed={selectedChip === value} disabled={locked || value > maximumChip} onClick={() => { onSelectChip(value); selectedButton.current?.focus() }} key={value}><img src={chipArt[value]} alt="" /></button>)}</div>}
  </div>
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
    <p>Place an Ante to receive three cards. Build your Play wager with chips or use Match Ante; it must equal your Ante. Keep enough credits for that matching wager. Fold gives up your Ante.</p>
    <div className="blackjack-help__actions"><span><strong>Play</strong>Challenge the dealer with your three-card hand.</span><span><strong>Fold</strong>End the hand without a Play wager.</span>{status.pairPlusAllowed && <span><strong>Pair Plus</strong>An optional side wager on a pair or better, regardless of the dealer. {status.pairPlusPaysOnFold ? 'It remains eligible when you fold.' : 'You must Play to remain eligible.'}</span>}<span><strong>Ante bonus</strong>A straight or better pays a bonus when you Play.</span></div>
    <p>Hand order: straight flush, three of a kind, straight, flush, pair, high card. A straight beats a flush in Three Card Poker.</p>
    <dl className="blackjack-help__rules"><div><dt>Dealer</dt><dd>{status.dealerRule}</dd></div><div><dt>No qualification</dt><dd>Ante wins 1:1; Play is returned</dd></div><div><dt>Dealer comparison</dt><dd>Ante and Play win 1:1; ties push</dd></div><div><dt>Ante bonus</dt><dd>{status.anteBonusPaytable}</dd></div>{status.pairPlusAllowed && <div><dt>Pair Plus</dt><dd>{status.pairPlusPaytable}</dd></div>}</dl>
  </section>}</div>
}
function formatLabel(value: string): string { return value.replaceAll('-', ' ').replace(/\b\w/g, (character) => character.toUpperCase()) }

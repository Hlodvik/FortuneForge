import { useCallback, useEffect, useId, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { InGameShell } from '../../../components/InGameShell'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import {
  actHouseHoldemRound, advanceHouseHoldemRound, affordableHouseHoldemAction, getHouseHoldemRound, getHouseHoldemStatus,
  houseHoldemMutationKey, isUncertainHouseHoldemFailure, startHouseHoldemRound,
  minimumHouseHoldemBankroll, toHouseHoldemPlayingCard, validHouseHoldemAnte, validHouseHoldemTrips,
  type HouseHoldemAction, type HouseHoldemCard, type HouseHoldemRound, type HouseHoldemStatus,
} from '../../../games/cards/houseHoldem/houseHoldemApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import { playCardAudio, useCardAudioClick } from '../../../games/cards/shared/cardAudio'
import '@fontsource-variable/sora'
import '../../../games/cards/shared/playingCards.css'
import '../blackjack/blackjack.css'
import '../threeCardPoker/threeCardPoker.css'
import './houseHoldem.css'
import chip1 from '../threeCardPoker/chip-1.png'
import chip2 from '../threeCardPoker/chip-2.png'
import chip5 from '../threeCardPoker/chip-5.png'
import chip10 from '../threeCardPoker/chip-10.png'
import chip25 from '../threeCardPoker/chip-25.png'

const storedRoundPrefix = 'fortune-forge.house-holdem.round.'
const chipValues = [1, 2, 5, 10, 25] as const
type ChipValue = typeof chipValues[number]
type Spot = 'ante' | 'trips'
type Placement = Readonly<{ spot: Spot; value: ChipValue }>
type PendingMutation = Readonly<{ fingerprint: string; key: string }>
type DealPresentation = Readonly<{ roundId: string; step: number }>
type LoadState = Readonly<{ kind: 'loading' }> | Readonly<{ kind: 'ready'; status: HouseHoldemStatus }> | Readonly<{ kind: 'error' | 'disabled'; message: string }>

const art: Record<ChipValue, string> = { 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25 }
const money = (value: number) => `R${value.toFixed(2)}`
const limitMoney = (value: number) => money(value).replace(/\.00$/, '')
const cents = (value: number) => Math.round(value * 100)

function readStoredRound(userId: string): string | null {
  try { return sessionStorage.getItem(`${storedRoundPrefix}${userId}`) } catch { return null }
}
function storeRound(userId: string, roundId: string) {
  try { sessionStorage.setItem(`${storedRoundPrefix}${userId}`, roundId) } catch { /* Storage can be disabled without preventing play. */ }
}
function clearStoredRound(userId: string) {
  try { sessionStorage.removeItem(`${storedRoundPrefix}${userId}`) } catch { /* Storage can be disabled. */ }
}

export function HouseHoldemPage({ account }: { account: AccountSummary }) {
  const [loadState, setLoadState] = useState<LoadState>({ kind: 'loading' })
  const [round, setRound] = useState<HouseHoldemRound | null>(null)
  const [balance, setBalance] = useState(account.balances.slotsCredits)
  const [ante, setAnte] = useState(0)
  const [trips, setTrips] = useState(0)
  const [placements, setPlacements] = useState<Placement[]>([])
  const [selectedChip, setSelectedChip] = useState<ChipValue>(1)
  const [chipPickerOpen, setChipPickerOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [pending, setPending] = useState<PendingMutation | null>(null)
  const [requestError, setRequestError] = useState<string | null>(null)
  const [dealPresentation, setDealPresentation] = useState<DealPresentation | null>(null)
  const busyRef = useRef(false)
  const pendingRef = useRef<PendingMutation | null>(null)
  const retryRef = useRef<{ fingerprint: string; send: (key: string) => Promise<HouseHoldemRound> } | null>(null)
  const onCardAudioClick = useCardAudioClick()

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoadState({ kind: 'loading' })
    try {
      const status = await getHouseHoldemStatus(signal)
      if (signal?.aborted) return
      if (!status.available) {
        setLoadState({ kind: 'disabled', message: 'The table is unavailable.' })
        return
      }
      const savedRoundId = status.activeRoundId ?? readStoredRound(account.userId)
      let savedRound: HouseHoldemRound | null = null
      if (savedRoundId) {
        try { savedRound = await getHouseHoldemRound(savedRoundId, signal) }
        catch (error) {
          if (signal?.aborted) return
          if (!isUncertainHouseHoldemFailure(error)) clearStoredRound(account.userId)
          else throw error
        }
      }
      if (signal?.aborted) return
      setRound(savedRound)
      setBalance(savedRound?.balance ?? status.balance)
      setLoadState({ kind: 'ready', status })
      setRequestError(null)
      pendingRef.current = null
      retryRef.current = null
      setPending(null)
    } catch {
      if (!signal?.aborted) setLoadState({ kind: 'error', message: 'The table could not be reached. No new wager was accepted.' })
    }
  }, [account.userId])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const recover = useCallback(async () => {
    const id = round?.roundId ?? readStoredRound(account.userId)
    if (!id) return false
    try {
      const latest = await getHouseHoldemRound(id)
      setRound(latest)
      setBalance(latest.balance)
      pendingRef.current = null
      retryRef.current = null
      setPending(null)
      setRequestError(null)
      return true
    } catch { return false }
  }, [account.userId, round?.roundId])

  const mutate = useCallback(async (fingerprint: string, send: (key: string) => Promise<HouseHoldemRound>) => {
    if (busyRef.current) return
    if (pendingRef.current && pendingRef.current.fingerprint !== fingerprint) {
      setRequestError('Refresh the table before choosing another action.')
      return
    }
    const mutation = pendingRef.current ?? { fingerprint, key: houseHoldemMutationKey() }
    pendingRef.current = mutation
    retryRef.current = { fingerprint, send }
    setPending(mutation)
    setRequestError(null)
    busyRef.current = true
    setBusy(true)
    try {
      const next = await send(mutation.key)
      if (fingerprint.startsWith('deal:') && next.phase === 'preflop') setDealPresentation({ roundId: next.roundId, step: 0 })
      setRound(next)
      setBalance(next.balance)
      storeRound(account.userId, next.roundId)
      pendingRef.current = null
      retryRef.current = null
      setPending(null)
    } catch (error) {
      const uncertain = isUncertainHouseHoldemFailure(error)
      if (!uncertain) {
        pendingRef.current = null
        retryRef.current = null
        setPending(null)
      }
      setRequestError(error instanceof Error ? error.message : 'The table could not complete that action.')
      if (uncertain) {
        // A timeout may have committed the wager. Reconcile before enabling another action.
        let id = readStoredRound(account.userId)
        if (!id) {
          try { id = (await getHouseHoldemStatus()).activeRoundId }
          catch { /* Retry preserves the original idempotency key. */ }
        }
        if (id) {
          try {
            const latest = await getHouseHoldemRound(id)
            if (fingerprint.startsWith('deal:') && latest.phase === 'preflop') setDealPresentation({ roundId: latest.roundId, step: 0 })
            setRound(latest)
            setBalance(latest.balance)
            pendingRef.current = null
            retryRef.current = null
            setPending(null)
            setRequestError(null)
          } catch { /* Keep the key for an exact retry. */ }
        }
      }
    } finally {
      busyRef.current = false
      setBusy(false)
    }
  }, [account.userId])

  const status = loadState.kind === 'ready' ? loadState.status : null
  const wagerPhase = status !== null && (round === null || round.phase === 'settled')
  const placeChip = (spot: Spot) => {
    if (!wagerPhase || busy || pending) return
    const nextAnte = ante + (spot === 'ante' ? selectedChip : 0)
    const nextTrips = trips + (spot === 'trips' ? selectedChip : 0)
    if (nextAnte > status.maximumAnte || nextTrips > status.maximumTrips || cents(minimumHouseHoldemBankroll(Math.max(nextAnte, status.minimumAnte), nextTrips)) > cents(balance)) return
    setPlacements((current) => [...current, { spot, value: selectedChip }])
    setAnte(nextAnte)
    setTrips(nextTrips)
  }
  const undo = () => {
    if (!wagerPhase || busy || pending || placements.length === 0) return
    const next = placements.slice(0, -1)
    setPlacements(next)
    setAnte(next.filter((item) => item.spot === 'ante').reduce((sum, item) => sum + item.value, 0))
    setTrips(next.filter((item) => item.spot === 'trips').reduce((sum, item) => sum + item.value, 0))
  }
  const deal = () => {
    if (!status || !validHouseHoldemAnte(ante, status) || !validHouseHoldemTrips(trips, status) || cents(minimumHouseHoldemBankroll(ante, trips)) > cents(balance)) return
    const fingerprint = `deal:${ante}:${trips}:${round?.roundId ?? 'new'}`
    void mutate(fingerprint, (key) => startHouseHoldemRound(ante, trips, key))
  }
  const dismissResult = () => {
    if (round?.phase !== 'settled' || busy || pending) return
    clearStoredRound(account.userId)
    setRound(null)
    setDealPresentation(null)
    setAnte(0)
    setTrips(0)
    setPlacements([])
    setChipPickerOpen(false)
  }
  const act = (action: HouseHoldemAction) => {
    if (!round || !affordableHouseHoldemAction(round, action)) return
    void mutate(`${round.roundId}:${round.phase}:${action}`, (key) => actHouseHoldemRound(round.roundId, action, key))
  }

  useEffect(() => {
    if (!round?.canAdvance || busy || pending || requestError) return
    const { roundId, phase } = round
    const timer = window.setTimeout(() => void mutate(`${roundId}:${phase}:advance`, (key) => advanceHouseHoldemRound(roundId, key)), 950)
    return () => window.clearTimeout(timer)
  }, [round, busy, pending, requestError, mutate])

  useEffect(() => {
    if (!dealPresentation) return
    const nextStep = dealPresentation.step + 1
    const timer = window.setTimeout(() => {
      if (nextStep > 4) setDealPresentation(null)
      else {
        setDealPresentation({ ...dealPresentation, step: nextStep })
        if (document.visibilityState === 'visible') playCardAudio('deal')
      }
    }, dealPresentation.step === 0 ? 160 : dealPresentation.step === 4 ? 310 : 240)
    return () => window.clearTimeout(timer)
  }, [dealPresentation])

  const cardCount = (round?.board.length ?? 0) + (round?.playerCards.length ?? 0) + (round?.dealerCards?.length ?? 0)
  const priorCardCount = useRef(cardCount)
  useEffect(() => {
    if (cardCount > priorCardCount.current && !dealPresentation && document.visibilityState === 'visible') playCardAudio('deal')
    priorCardCount.current = cardCount
  }, [cardCount, dealPresentation])
  const priorPhase = useRef(round?.phase)
  useEffect(() => {
    if (round?.phase === 'settled' && priorPhase.current !== 'settled' && document.visibilityState === 'visible') playCardAudio('collect')
    priorPhase.current = round?.phase
  }, [round?.phase])

  const navbarAccount = { ...account, balances: { ...account.balances, slotsCredits: balance } }
  return <InGameShell account={navbarAccount} title="House Hold’em" theme="cards" className="blackjack-game-shell three-card-poker-shell house-holdem-shell" bodyClassName="blackjack-shell-body"
    actions={status ? <HouseHoldemHowToPlay status={status} /> : undefined}>
    <div className="blackjack-page blackjack-table-page three-card-poker-page house-holdem-page" onClickCapture={onCardAudioClick}>
      {requestError && <div className="blackjack-error" role="alert"><span>{requestError}</span><button type="button" disabled={busy} onClick={() => {
        const retry = retryRef.current
        if (pending && retry?.fingerprint === pending.fingerprint) void mutate(retry.fingerprint, retry.send)
        else void recover().then((recovered) => { if (!recovered) void load() })
      }}>{pending ? 'Retry request' : 'Refresh table'}</button></div>}
      <HouseHoldemContent loadState={loadState} round={round} balance={balance} ante={ante} trips={trips} placements={placements} selectedChip={selectedChip}
        chipPickerOpen={chipPickerOpen} busy={busy} pending={pending} dealPresentation={dealPresentation}
        onDismissResult={dismissResult} onPlace={placeChip} onUndo={undo} onDeal={deal} onAct={act}
        onSelectChip={(value) => { setSelectedChip(value); setChipPickerOpen(false) }} onTogglePicker={() => setChipPickerOpen((value) => !value)} onRefresh={() => void load()} />
    </div>
  </InGameShell>
}

type ContentProps = Readonly<{
  loadState: LoadState; round: HouseHoldemRound | null; balance: number; ante: number; trips: number
  placements: readonly Placement[]; selectedChip: ChipValue; chipPickerOpen: boolean; busy: boolean; pending: PendingMutation | null
  dealPresentation: DealPresentation | null; onDismissResult: () => void
  onPlace: (spot: Spot) => void; onUndo: () => void; onDeal: () => void; onAct: (action: HouseHoldemAction) => void
  onSelectChip: (value: ChipValue) => void; onTogglePicker: () => void; onRefresh: () => void
}>

export function HouseHoldemContent(props: ContentProps) {
  const { loadState, round } = props
  if (loadState.kind === 'loading') return <main className="blackjack-main blackjack-state" role="status">Opening table…</main>
  if (loadState.kind !== 'ready') return <main className="blackjack-main blackjack-state" role={loadState.kind === 'error' ? 'alert' : 'status'}><h1>{loadState.kind === 'error' ? 'The table is offline.' : 'The table is unavailable.'}</h1><p>{loadState.message}</p><button type="button" onClick={props.onRefresh}>Try again</button></main>
  const status = loadState.status
  const waitingForDeal = props.pending?.fingerprint.startsWith('deal:') ?? false
  const shownRound = waitingForDeal ? null : round
  const dealing = props.dealPresentation?.roundId === round?.roundId && round?.phase === 'preflop' ? props.dealPresentation : null
  const playerVisibleCount = dealing ? (dealing.step >= 4 ? 2 : dealing.step >= 3 ? 1 : 0) : 2
  const dealerVisibleCount = dealing ? Math.min(dealing.step, 2) : 2
  const betting = round === null || round.phase === 'settled'
  const shownAnte = betting ? props.ante : round.ante
  const shownTrips = betting ? props.trips : round.trips
  const anteChips = betting ? props.placements.filter((item) => item.spot === 'ante').map((item) => item.value) : chipsForAmount(round.ante)
  const tripsChips = betting ? props.placements.filter((item) => item.spot === 'trips').map((item) => item.value) : chipsForAmount(round.trips)
  const canPlaceAnte = betting && !props.busy && !props.pending && shownAnte + props.selectedChip <= status.maximumAnte && cents(minimumHouseHoldemBankroll(Math.max(shownAnte + props.selectedChip, status.minimumAnte), shownTrips)) <= cents(props.balance)
  const canPlaceTrips = betting && !props.busy && !props.pending && shownTrips + props.selectedChip <= status.maximumTrips && cents(minimumHouseHoldemBankroll(Math.max(shownAnte, status.minimumAnte), shownTrips + props.selectedChip)) <= cents(props.balance)
  const canDeal = betting && !props.busy && !props.pending && validHouseHoldemAnte(props.ante, status) && validHouseHoldemTrips(props.trips, status) && cents(minimumHouseHoldemBankroll(props.ante, props.trips)) <= cents(props.balance)
  const controls = dealing || waitingForDeal ? [] : round?.allowedActions ?? []
  return <main className="blackjack-main blackjack-game"><section className="blackjack-table" aria-label="House Hold’em table" data-phase={dealing || waitingForDeal ? 'dealing' : round?.phase ?? 'betting'} onClickCapture={round?.phase === 'settled' && !props.busy && !props.pending ? (event) => {
    event.preventDefault()
    event.stopPropagation()
    props.onDismissResult()
  } : undefined}>
    <div className="blackjack-playfield">
      <div className="three-card-poker-limits">MIN ANTE {limitMoney(status.minimumAnte)} <span>•</span> MAX ANTE {limitMoney(status.maximumAnte)}</div>
      <div className="blackjack-dealer"><CardGroup label="Dealer" cards={shownRound?.dealerCards ?? null} showBacks={shownRound !== null && shownRound.phase !== 'settled'} visibleCount={dealerVisibleCount} count={2} scope="dealer" handLabel={shownRound?.dealerHand ?? null} /></div>
      <div className="house-holdem-board" role="group" aria-label="Community cards"><CardGroup label="" cards={shownRound?.board ?? []} count={5} scope="board" /></div>
      <div className="three-card-poker-felt house-holdem-felt" aria-hidden="true"><strong>House Hold’em</strong><span className="house-holdem-felt__underline" /></div>
      <div className="three-card-poker-player" role="group" aria-label="Your hand"><CardGroup label="" cards={shownRound?.playerCards ?? []} showBacks={!!dealing} visibleCount={playerVisibleCount} concealCards={!!dealing} count={2} scope="player" handLabel={dealing ? null : shownRound?.playerHand ?? null} /></div>
      <div className="three-card-poker-bets" aria-label="Betting spots">
        <BetSpot kind="trips" label="Trips" amount={shownTrips} chips={tripsChips} onPlace={canPlaceTrips ? () => props.onPlace('trips') : undefined} disabled={!canPlaceTrips} />
        <BetSpot kind="ante" label="Ante" amount={shownAnte} chips={anteChips} onPlace={canPlaceAnte ? () => props.onPlace('ante') : undefined} disabled={!canPlaceAnte} />
        <BetSpot kind="blind" label="Blind" amount={betting ? props.ante : round.blind} chips={betting ? anteChips : chipsForAmount(round.blind)} disabled />
        <BetSpot kind="play" label="Wager" amount={shownRound?.play ?? 0} chips={chipsForAmount(shownRound?.play ?? 0)} disabled />
      </div>
    </div>
    {shownRound?.phase === 'settled' && <RoundResult round={shownRound} onDismiss={props.onDismissResult} />}
    <div className="blackjack-actions" aria-label="House Hold’em controls">
      <div className="three-card-poker-action-stats"><span>Bet <strong>{money(shownRound ? shownRound.totalWager : props.ante * 2 + props.trips)}</strong></span><span>Balance <strong>{money(props.balance)}</strong></span><span>Win <strong>{money(shownRound?.phase === 'settled' ? shownRound.payout : 0)}</strong></span></div>
      {betting ? <div className="three-card-poker-wager-wrap">
        <button className="three-card-poker-action-orb three-card-poker-action-orb--undo" type="button" aria-label="Undo chip" disabled={props.busy || !!props.pending || props.placements.length === 0} onClick={props.onUndo}><CloseIcon /></button>
        <div className="three-card-poker-chip-picker" role="group" aria-label="Chips">
          <button className="three-card-poker-chip-picker__selected" type="button" aria-label={`Choose chip value, currently ${money(props.selectedChip)}`} aria-expanded={props.chipPickerOpen} disabled={props.busy || !!props.pending} onClick={props.onTogglePicker}><img src={art[props.selectedChip]} alt="" /></button>
          {props.chipPickerOpen && <div className="three-card-poker-chip-picker__options">{chipValues.map((value) => <button className={`three-card-poker-chip-option${props.selectedChip === value ? ' is-selected' : ''}`} type="button" aria-label={`Select ${money(value)} chip`} aria-pressed={props.selectedChip === value} disabled={props.busy || !!props.pending || value > Math.max(status.maximumAnte, status.maximumTrips) || value > props.balance} onClick={() => props.onSelectChip(value)} key={value}><img src={art[value]} alt="" /></button>)}</div>}
        </div>
        <button className="three-card-poker-action-orb three-card-poker-action-orb--deal" type="button" aria-label="Deal" disabled={!canDeal} onClick={props.onDeal}><CardStackIcon /></button>
      </div> : controls.length > 0 ? <div className="house-holdem-decision-actions">{controls.map((action) => <button type="button" className={`house-holdem-action house-holdem-action--${action}`} aria-label={actionLabel(action)} disabled={props.busy || !!props.pending || !affordableHouseHoldemAction(round, action)} onClick={() => props.onAct(action)} key={action}>{action === 'fold' ? <CloseIcon /> : shortActionLabel(action)}</button>)}</div> : null}
    </div>
  </section></main>
}

function CardGroup({ label, cards, count, scope, showBacks = false, visibleCount = count, concealCards = false, handLabel }: {
  label: string; cards: readonly HouseHoldemCard[] | null; count: number; scope: string; showBacks?: boolean; visibleCount?: number; concealCards?: boolean; handLabel?: string | null
}) {
  return <div className="blackjack-hand">
    {(label || handLabel) && <div className="blackjack-hand__heading">{label && <h2>{label}</h2>}{handLabel && <small>{formatLabel(handLabel)}</small>}</div>}
    <div className="blackjack-hand__cards">{Array.from({ length: count }, (_, index) => {
      const card = cards?.[index]
      if (index >= visibleCount) return <span className="three-card-poker-card-outline" key={`${scope}-outline-${index}`} aria-hidden="true" />
      if (concealCards && card) return <span className="ff-card-slot blackjack-card-enter" key={`${scope}-hidden-${index}`}><PlayingCard card={{ id: `${scope}-hidden-${index}`, rank: 1, suit: 'spades' }} faceDown /></span>
      if (card) return <span className="ff-card-slot blackjack-card-enter" key={`${scope}-${index}-${card.rank}-${card.suit}`}><PlayingCard card={toHouseHoldemPlayingCard(card, scope, index)} /></span>
      if (showBacks) return <span className="ff-card-slot blackjack-card-enter" key={`${scope}-hidden-${index}`}><PlayingCard card={{ id: `${scope}-hidden-${index}`, rank: 1, suit: 'spades' }} faceDown /></span>
      return <span className="three-card-poker-card-outline" key={`${scope}-outline-${index}`} aria-hidden="true" />
    })}</div>
  </div>
}

function BetSpot({ kind, label, amount, chips, onPlace, disabled }: { kind: 'trips' | 'ante' | 'blind' | 'play'; label: string; amount: number; chips: readonly ChipValue[]; onPlace?: () => void; disabled: boolean }) {
  const tipId = useId()
  const help = {
    trips: 'Optional side bet on your final five-card hand. It can pay even if you fold.',
    ante: 'Your required opening bet against the dealer. Blind automatically matches it.',
    blind: 'Automatically matches your Ante. A strong hand that beats the dealer can earn a bonus.',
    play: 'Your Play bet: 3× or 4× Ante before the flop, 2× after it, or 1× at the river.',
  }[kind]
  const contents = <><span className="three-card-poker-chip-stack" aria-hidden="true">{chips.slice(-4).map((value, index) => <img className="three-card-poker-chip" src={art[value]} alt="" style={{ '--chip-index': index } as CSSProperties} key={`${kind}-${index}-${value}`} />)}</span><strong className="three-card-poker-bet-spot__amount">{money(amount)}</strong></>
  const className = `three-card-poker-bet-spot house-holdem-bet-spot house-holdem-bet-spot--${kind}`
  const spot = onPlace ? <button className={className} type="button" aria-label={`Place chip on ${label}`} aria-describedby={tipId} disabled={disabled} onClick={onPlace} data-active="true" data-empty={chips.length === 0}>{contents}</button>
    : <div className={className} aria-label={kind === 'play' ? 'Wager spot' : `${label} wager`} aria-describedby={tipId} data-empty={chips.length === 0}>{contents}</div>
  return <div className="house-holdem-bet-spot-wrap">
    {spot}
    <button className="three-card-poker-bet-spot__label house-holdem-bet-spot__help" type="button" aria-label={`About ${label}`} aria-describedby={tipId}>{label}</button>
    <span className="house-holdem-bet-spot__tip" id={tipId} role="tooltip">{help}</span>
  </div>
}

function RoundResult({ round, onDismiss }: { round: HouseHoldemRound; onDismiss: () => void }) {
  const net = cents(round.payout - round.totalWager) / 100
  const title = net > 0 ? 'Win' : net < 0 ? 'Loss' : 'Push'
  const breakdown = round.payoutBreakdown
  return <div className={`three-card-poker-result three-card-poker-result--${title.toLowerCase()}`} role="status" aria-label="Round result">
    <button className="house-holdem-result-close" type="button" aria-label="Dismiss result" onClick={onDismiss}><CloseIcon /></button>
    <strong className="three-card-poker-result__title">{title}</strong>
    {breakdown && <dl><div><dt>Ante</dt><dd>{money(breakdown.ante)}</dd></div><div><dt>Blind</dt><dd>{money(breakdown.blind)}</dd></div><div><dt>Trips</dt><dd>{money(breakdown.trips)}</dd></div><div><dt>Wager</dt><dd>{money(breakdown.play)}</dd></div></dl>}
    <strong className="three-card-poker-result__net">{net >= 0 ? '+' : '−'}{money(Math.abs(net))}</strong>
  </div>
}

function chipsForAmount(amount: number): ChipValue[] {
  if (!Number.isInteger(amount) || amount <= 0) return []
  const chips: ChipValue[] = []
  let remaining = amount
  for (const value of [...chipValues].reverse()) {
    while (remaining >= value && chips.length < 24) { chips.push(value); remaining -= value }
  }
  return chips
}

function shortActionLabel(action: HouseHoldemAction): ReactNode {
  if (action === 'check') return 'Check'
  if (action === 'fold') return 'Fold'
  return `${action.slice(-2, -1)}×`
}
function actionLabel(action: HouseHoldemAction): string {
  if (action === 'check') return 'Check'
  if (action === 'fold') return 'Fold'
  const multiplier = action.slice(-2, -1)
  return `Wager ${multiplier} ${multiplier === '1' ? 'time' : 'times'} your Ante`
}
function formatLabel(value: string): string { return value.replaceAll('-', ' ').replace(/\b\w/g, (character) => character.toUpperCase()) }

function CardStackIcon() {
  return <svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><rect x="12" y="11" width="38" height="43" rx="4" fill="#062c37" stroke="#c8fff8" strokeWidth="3" transform="rotate(-11 31 32)"/><rect x="17" y="11" width="38" height="43" rx="4" fill="#f4fcf6" stroke="#0d5560" strokeWidth="2.5"/><path d="M36 18l12 15-12 15-12-15z" fill="#267797"/><path d="M36 24l7 9-7 9-7-9z" fill="#bce9ee"/></svg>
}
function CloseIcon() {
  return <svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><path d="M21 21 43 43M43 21 21 43" fill="none" stroke="currentColor" strokeWidth="7" strokeLinecap="round" /></svg>
}

const blindPays = [
  ['Royal flush', '500:1'], ['Straight flush', '50:1'], ['Four of a kind', '10:1'],
  ['Full house', '3:1'], ['Flush', '3:2'], ['Straight', '1:1'],
] as const
const tripsPays = [
  ['Royal flush', '50:1'], ['Straight flush', '40:1'], ['Four of a kind', '30:1'],
  ['Full house', '9:1'], ['Flush', '7:1'], ['Straight', '4:1'], ['Three of a kind', '3:1'],
] as const

export function HouseHoldemHowToPlay({ status, defaultOpen = false }: { status: HouseHoldemStatus; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  const id = useId()
  const container = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const close = () => { setOpen(false); trigger.current?.focus() }
  useEffect(() => {
    if (!open) return
    closeButton.current?.focus()
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') close() }
    const outside = (event: PointerEvent) => { if (!container.current?.contains(event.target as Node)) setOpen(false) }
    document.addEventListener('keydown', escape)
    document.addEventListener('pointerdown', outside)
    return () => { document.removeEventListener('keydown', escape); document.removeEventListener('pointerdown', outside) }
  }, [open])
  return <div className="blackjack-help" ref={container}><button className="blackjack-help__button" ref={trigger} type="button" aria-label="How to play House Hold’em" aria-expanded={open} aria-controls={id} onClick={() => setOpen((value) => !value)}>?</button>{open && <section className="blackjack-help__panel" id={id} role="dialog" aria-label="How to play House Hold’em">
    <header><div><small>House Hold’em guide</small><h2>How to play</h2></div><button ref={closeButton} type="button" aria-label="Close how to play" onClick={close}>×</button></header>
    <p>Place an Ante and a matching Blind to receive two cards. Trips is an optional wager on the strength of your final five-card hand.</p>
    <div className="blackjack-help__actions"><span><strong>Before the flop</strong>Check or wager 3× or 4× your Ante.</span><span><strong>After the flop</strong>If you checked, check again or wager 2× your Ante.</span><span><strong>At the river</strong>If you have not wagered, wager 1× your Ante or fold.</span></div>
    <p>Your best five-card hand uses any combination of your two cards and the five community cards. The dealer must have at least a pair to qualify. The Blind and Trips wagers pay according to their hand paytables.</p>
    <dl className="blackjack-help__rules"><div><dt>Ante</dt><dd>{money(status.minimumAnte)}–{money(status.maximumAnte)}</dd></div><div><dt>Blind</dt><dd>Matches your Ante</dd></div><div><dt>Trips</dt><dd>Optional, up to {money(status.maximumTrips)}</dd></div></dl>
    <div className="house-holdem-guide-paytables">
      <section aria-label="Blind payouts"><h3>Blind payouts</h3><dl>{blindPays.map(([hand, odds]) => <div key={hand}><dt>{hand}</dt><dd>{odds}</dd></div>)}</dl><p>Pays only when you beat the dealer. A lower winning hand or tie returns the Blind.</p></section>
      <section aria-label="Trips payouts"><h3>Trips payouts</h3><dl>{tripsPays.map(([hand, odds]) => <div key={hand}><dt>{hand}</dt><dd>{odds}</dd></div>)}</dl><p>Based on your final hand, even if you fold. Lower hands lose.</p></section>
    </div>
  </section>}</div>
}

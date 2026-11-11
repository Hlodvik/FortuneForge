import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import {
  HouseHoldemContent as BundledHouseHoldemContent, HouseHoldemGame as BundledHouseHoldemGame,
  type HouseHoldemBetSpot as Spot, type HouseHoldemChipValue as ChipValue, type HouseHoldemContentProps,
  type HouseHoldemDealPresentation as DealPresentation, type HouseHoldemLoadState as LoadState,
  type HouseHoldemPlacement as Placement,
} from '@fortuneforge/games-house-holdem'
import bundledStyles from '@fortuneforge/games-house-holdem/styles.css?inline'
import { useReleasedGame } from '../../../features/game-releases/useReleasedGame'
import { InGameShell } from '../../../components/InGameShell'
import { GameSupportControls } from '../../../components/GameSupportControls'
import { GameLoadingScreen } from '../../../features/game-loading/GameLoadingScreen'
import { resolveGameLoadingEntry } from '../../../features/game-loading/gameLoadingRegistry'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { useGameLeaveGuard } from '../../../features/navigation/GameLeaveGuard'
import {
  actHouseHoldemRound, advanceHouseHoldemRound, affordableHouseHoldemAction, getHouseHoldemRound, getHouseHoldemStatus,
  houseHoldemMutationKey, isUncertainHouseHoldemFailure, startHouseHoldemRound,
  minimumHouseHoldemBankroll, toHouseHoldemPlayingCard, validHouseHoldemAnte, validHouseHoldemTrips,
  type HouseHoldemAction, type HouseHoldemCard, type HouseHoldemRound,
} from '../../../games/cards/houseHoldem/houseHoldemApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import { playCardAudio, useCardAudioClick } from '../../../games/cards/shared/cardAudio'
import '@fontsource-variable/sora'
import '../../../games/cards/shared/playingCards.css'
import '../blackjack/blackjack.css'
import '../threeCardPoker/threeCardPoker.css'
import chip1 from '../threeCardPoker/chip-1.png'
import chip2 from '../threeCardPoker/chip-2.png'
import chip5 from '../threeCardPoker/chip-5.png'
import chip10 from '../threeCardPoker/chip-10.png'
import chip25 from '../threeCardPoker/chip-25.png'

const storedRoundPrefix = 'fortune-forge.house-holdem.round.'
type PendingMutation = Readonly<{ fingerprint: string; key: string }>
type LocalContentProps = Omit<HouseHoldemContentProps,
  'requestPending' | 'waitingForDeal' | 'loading' | 'renderCard' | 'chipImages'> & {
  pending: PendingMutation | null
}

const chipImages: Record<ChipValue, string> = { 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25 }
const cents = (value: number) => Math.round(value * 100)

function renderCard(card: HouseHoldemCard | null, scope: string, index: number) {
  if (card) return <PlayingCard card={toHouseHoldemPlayingCard(card, scope, index)} />
  return <PlayingCard card={{ id: `${scope}-hidden-${index}`, rank: 1, suit: 'spades' }} faceDown />
}

function renderSupportControls(guide: ReactNode) {
  return <div className="house-holdem-local-controls"><GameSupportControls>{guide}</GameSupportControls></div>
}

function viewProps({ pending, ...content }: LocalContentProps): HouseHoldemContentProps {
  return {
    ...content,
    requestPending: pending !== null,
    waitingForDeal: pending?.fingerprint.startsWith('deal:') ?? false,
    loading: <GameLoadingScreen entry={resolveGameLoadingEntry('/cards/house-holdem')} />,
    renderCard,
    chipImages,
  }
}

export function HouseHoldemContent(props: LocalContentProps) {
  return <BundledHouseHoldemContent {...viewProps(props)} />
}

export { HouseHoldemHowToPlay } from '@fortuneforge/games-house-holdem'

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
  const [showOpeningBetHint, setShowOpeningBetHint] = useState(true)
  const [busy, setBusy] = useState(false)
  const [pending, setPending] = useState<PendingMutation | null>(null)
  const [requestError, setRequestError] = useState<string | null>(null)
  const [dealPresentation, setDealPresentation] = useState<DealPresentation | null>(null)
  const busyRef = useRef(false)
  const pendingRef = useRef<PendingMutation | null>(null)
  const retryRef = useRef<{ fingerprint: string; send: (key: string) => Promise<HouseHoldemRound> } | null>(null)
  const onCardAudioClick = useCardAudioClick()
  const hasSavedUnresolvedHand = round === null && loadState.kind !== 'ready' && readStoredRound(account.userId) !== null
  useGameLeaveGuard((round !== null && round.phase !== 'settled') || pending !== null || hasSavedUnresolvedHand ? {
    title: 'Leave House Hold’em?',
    description: pending !== null
      ? 'A table request is still being confirmed and may already have been accepted. Leaving does not cancel it; return to check or resume the hand.'
      : hasSavedUnresolvedHand
        ? 'Your saved wagered hand has not been reconciled yet. Leaving does not settle it; return to check its state.'
        : 'Your wagered hand stays open when you leave. Return to resume it. Leaving this page does not fold, refund or settle the hand.',
  } : null)

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
      if (savedRound) setShowOpeningBetHint(false)
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
    if (spot === 'ante') setShowOpeningBetHint(false)
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

  const { Game: HouseHoldemGame, pending: releasePending } = useReleasedGame(
    'house-holdem', 'House Hold’em', BundledHouseHoldemGame, bundledStyles,
  )
  const navbarAccount = { ...account, balances: { ...account.balances, slotsCredits: balance } }
  return <InGameShell account={navbarAccount} title="House Hold’em" className="blackjack-game-shell three-card-poker-shell house-holdem-shell" bodyClassName="blackjack-shell-body">
    <div className="blackjack-page blackjack-table-page three-card-poker-page house-holdem-page" onClickCapture={onCardAudioClick}>
      {requestError && <div className="blackjack-error" role="alert"><span>{requestError}</span><button type="button" disabled={busy} onClick={() => {
        const retry = retryRef.current
        if (pending && retry?.fingerprint === pending.fingerprint) void mutate(retry.fingerprint, retry.send)
        else void recover().then((recovered) => { if (!recovered) void load() })
      }}>{pending ? 'Retry request' : 'Refresh table'}</button></div>}
      {HouseHoldemGame === null ? releasePending : <HouseHoldemGame
        {...viewProps({
          loadState, round, balance, ante, trips, placements, selectedChip, showOpeningBetHint,
          chipPickerOpen, busy, pending, dealPresentation, onDismissResult: dismissResult, onPlace: placeChip,
          onUndo: undo, onDeal: deal, onAct: act,
          onSelectChip: (value) => { setSelectedChip(value); setChipPickerOpen(false) },
          onTogglePicker: () => setChipPickerOpen((value) => !value),
          onRefresh: () => void load(),
        })}
        renderSupportControls={renderSupportControls}
      />}
    </div>
  </InGameShell>
}

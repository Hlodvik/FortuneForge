import { useCallback, useEffect, useRef, useState } from 'react'
import {
  ThreeCardPokerContent as BundledContent, ThreeCardPokerHowToPlay, type Availability,
  type ThreeCardPokerBrowserViews, type ThreeCardPokerContentProps, type ThreeCardPokerViewServices,
} from '@fortuneforge/games-three-card-poker'
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
  type PendingThreeCardPokerMutation, type ThreeCardPokerAction, type ThreeCardPokerMutationResponse,
  type ThreeCardPokerPlaySession, type ThreeCardPokerQueueSession,
} from '../../../games/cards/threeCardPoker/threeCardPokerApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import { playCardAudio, useCardAudioClick } from '../../../games/cards/shared/cardAudio'
import '@fontsource-variable/sora'
import '../../../games/cards/shared/playingCards.css'
import '../blackjack/blackjack.css'
import chip1 from './chip-1.png'
import chip2 from './chip-2.png'
import chip5 from './chip-5.png'
import chip10 from './chip-10.png'
import chip25 from './chip-25.png'

const viewServices: ThreeCardPokerViewServices = {
  renderLoading: () => <GameLoadingScreen entry={resolveGameLoadingEntry('/cards/three-card-poker')} statusLabel="Loading" />,
  renderCard: (card, index, scope) => {
    const playingCard = toThreeCardPokerPlayingCard(card, index, scope)
    return <PlayingCard card={playingCard ?? { id: scope + '-hidden-' + index, suit: 'spades', rank: 1 }} faceDown={playingCard === null} />
  },
  playAudio: playCardAudio,
  chipArt: { 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25 },
}

const bundledViews: ThreeCardPokerBrowserViews = { Content: BundledContent, HowToPlay: ThreeCardPokerHowToPlay }

export { ThreeCardPokerHowToPlay }

export function ThreeCardPokerContent(props: Omit<ThreeCardPokerContentProps, 'services'>) {
  return <BundledContent {...props} services={viewServices} />
}

export function ThreeCardPokerPage({ account, views = bundledViews }: { account: AccountSummary; views?: ThreeCardPokerBrowserViews }) {
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
  return <InGameShell account={navbarAccount} title="Three Card Poker" className="blackjack-game-shell three-card-poker-shell" bodyClassName="blackjack-shell-body">
    {availability.kind === 'ready' && <div className="game-local-controls"><views.HowToPlay status={availability.status} /></div>}
    <div className="blackjack-page blackjack-table-page three-card-poker-page" onClickCapture={onCardAudioClick}>
      {requestError && <div className="blackjack-error" role="alert"><span>{requestError}</span><button type="button" disabled={busy} onClick={() => { setPending(null); void load() }}>Refresh table</button></div>}
      <views.Content services={viewServices} availability={availability} balanceCredits={balanceCredits} ante={ante} pairPlus={pairPlus} busy={busy} pending={pending}
        onAnteChange={setAnte} onPairPlusChange={setPairPlus} onJoin={join} onCancel={cancel} onWager={wager} onSitOut={sitOut} onAction={act} onLeave={leave} onRefresh={() => void load()} />
    </div>
  </InGameShell>
}

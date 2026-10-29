import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import {
  BaccaratGatewayError, type BaccaratBetSide, type BaccaratCard,
  type BaccaratGateway, type BaccaratRound, type BaccaratStatus,
} from './contracts'
import { HttpBaccaratGateway } from './httpBaccaratGateway'
import { buildBigRoad, buildDerivedRoads, type BaccaratHistoryItem } from './baccaratRoad'
import { playBaccaratChip, readBaccaratMuted, stopBaccaratAudio, writeBaccaratMuted } from './baccaratAudio'
import chip025 from './chip-025.png'
import chip050 from './chip-050.png'
import chip1 from './chip-1.png'
import chip2 from './chip-2.png'
import chip5 from './chip-5.png'
import chip10 from './chip-10.png'
import chip25 from './chip-25.png'
import chip50 from './chip-50.png'
import blackLeather from './baccarat-black-leather-generated.png'
import { BankerChipRack } from './BankerChipRack'
import './baccarat.css'
import './baccaratRoad.css'
import './baccaratViewport.css'
import './baccaratTable.css'
import './baccaratBankerRack.css'
import './baccaratResult.css'
import './baccaratAudio.css'

export type BaccaratGameProps = Readonly<{
  gateway?: BaccaratGateway
  playerId?: string
  currencySymbol?: string
  onBalanceChange?: (balance: number) => void
  showTitle?: boolean
  renderCardFace?: (card: BaccaratCard) => ReactNode
}>

const defaultGateway = new HttpBaccaratGateway()
type StoredPendingDeal = Readonly<{ idempotencyKey: string; betSide: BaccaratBetSide; stake: number; playerPairStake?: number; bankerPairStake?: number; placedChips?: PlacedChips }>
const chipOptions = [
  // The source PNGs share a 64px canvas but not the same painted diameter.
  { value: .25, image: chip025, scale: 60 / 53 }, { value: .5, image: chip050, scale: 60 / 57 },
  { value: 1, image: chip1, scale: 1 }, { value: 2, image: chip2, scale: 1 },
  { value: 5, image: chip5, scale: 1 }, { value: 10, image: chip10, scale: 1 },
  { value: 25, image: chip25, scale: 1 }, { value: 50, image: chip50, scale: 60 / 51 },
] as const
type ChipValue = (typeof chipOptions)[number]['value']
type PlacedChips = Readonly<{ main: readonly ChipValue[]; playerPair: readonly ChipValue[]; bankerPair: readonly ChipValue[] }>
const emptyPlacedChips = (): PlacedChips => ({ main: [], playerPair: [], bankerPair: [] })

export function BaccaratGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange, showTitle = true, renderCardFace }: BaccaratGameProps) {
  const [status, setStatus] = useState<BaccaratStatus | null>(null)
  const [round, setRound] = useState<BaccaratRound | null>(null)
  const [betSide, setBetSide] = useState<BaccaratBetSide>('player')
  const [stake, setStake] = useState(0)
  const [playerPairStake, setPlayerPairStake] = useState(0)
  const [bankerPairStake, setBankerPairStake] = useState(0)
  const [placedChips, setPlacedChips] = useState<PlacedChips>(emptyPlacedChips)
  const [selectedChipValue, setSelectedChipValue] = useState<ChipValue>(.25)
  const [muted, setMuted] = useState(readBaccaratMuted)
  const [chipWindowStart, setChipWindowStart] = useState(0)
  const [compactChipSelector, setCompactChipSelector] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [history, setHistory] = useState<readonly BaccaratHistoryItem[]>([])
  const [revealedCards, setRevealedCards] = useState(0)
  const [revealFinished, setRevealFinished] = useState(false)
  const [shoeUsed, setShoeUsed] = useState(0)
  const [shoeRemaining, setShoeRemaining] = useState(416)
  const [shoeVerified, setShoeVerified] = useState(false)
  const [lastBalance, setLastBalance] = useState<number | null>(null)
  const [activePanel, setActivePanel] = useState<string | null>(null)
  const dealRequestKey = useRef<string | null>(null)
  const pendingBet = useRef<Readonly<{ side: BaccaratBetSide; stake: number; playerPairStake: number; bankerPairStake: number }> | null>(null)
  const gameRef = useRef<HTMLElement>(null)
  const playSurfaceRef = useRef<HTMLDivElement>(null)
  const historyRef = useRef(history)
  const shoeUsedRef = useRef(shoeUsed)
  const recordedRounds = useRef(new Set<string>())

  useEffect(() => () => stopBaccaratAudio(), [])

  useEffect(() => {
    const surface = playSurfaceRef.current
    if (!surface) return
    const updateChipSize = () => {
      const compact = window.innerWidth <= 480 || window.innerHeight <= 560 && window.innerWidth > window.innerHeight
      setCompactChipSelector(compact)
      const board = surface.querySelector<HTMLElement>('.ff-baccarat__bet-sides')
      const { width, height } = board?.getBoundingClientRect() ?? surface.getBoundingClientRect()
      if (width <= 0 || height <= 0) return
      const chipSize = compact ? Math.min(40, Math.max(38, width * .1)) : Math.min(44, Math.max(40, width * .032))
      surface.style.setProperty('--ff-chip-screen-size', `${chipSize}px`)
      surface.style.setProperty('--ff-board-chip-size', `${chipSize}px`)
    }
    updateChipSize()
    window.addEventListener('resize', updateChipSize)
    if (typeof ResizeObserver === 'undefined') return () => window.removeEventListener('resize', updateChipSize)
    const observer = new ResizeObserver(updateChipSize)
    observer.observe(surface)
    return () => { observer.disconnect(); window.removeEventListener('resize', updateChipSize) }
  }, [])

  const visibleChipCount = compactChipSelector ? 3 : 5
  const maxChipWindowStart = chipOptions.length - visibleChipCount
  const visibleChipWindowStart = Math.min(chipWindowStart, maxChipWindowStart)
  useEffect(() => {
    setChipWindowStart(start => {
      let next = Math.min(start, maxChipWindowStart)
      const selectedIndex = chipOptions.findIndex(chip => chip.value === selectedChipValue)
      if (selectedIndex < next) next = selectedIndex
      if (selectedIndex >= next + visibleChipCount) next = Math.min(maxChipWindowStart, selectedIndex - visibleChipCount + 1)
      return next
    })
  }, [visibleChipCount, maxChipWindowStart, selectedChipValue])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setStake(current => current === 0 || current >= nextStatus.minimumStake && current <= nextStatus.maximumStake && isIncrementAligned(current, nextStatus.minimumStake, nextStatus.stakeIncrement) ? current : 0)
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
    setPlayerPairStake(pendingDeal.playerPairStake ?? 0)
    setBankerPairStake(pendingDeal.bankerPairStake ?? 0)
    setPlacedChips(pendingDeal.placedChips ?? emptyPlacedChips())
    const controller = new AbortController()
    setRecovery('recovering')
    void gateway.createRound(pendingDeal.betSide, pendingDeal.stake, {
      signal: controller.signal, idempotencyKey: pendingDeal.idempotencyKey,
      playerPairStake: pendingDeal.playerPairStake ?? 0, bankerPairStake: pendingDeal.bankerPairStake ?? 0,
    }).then(nextRound => {
      setRound(nextRound)
      setRecovery('ready')
      setError(null)
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      if (isRejectedDeal(reason)) {
        clearPendingDeal(playerId)
        setRecovery('ready')
        setError(messageForError(reason))
        setLastBalance(null); setStatus(null)
        setStatusLoadAttempt(attempt => attempt + 1)
        return
      }
      setRecovery('failed')
      setError('Your pending Baccarat deal could not be restored. Retry restoration before starting another hand.')
    })
    return () => controller.abort()
  }, [gateway, playerId, recoveryAttempt])

  useEffect(() => {
    // Roads belong to the open table. A new visit starts a fresh display;
    // pending paid deals still recover separately via sessionStorage.
    clearStoredRoadmap(playerId)
    historyRef.current = []
    shoeUsedRef.current = 0
    recordedRounds.current = new Set()
    setHistory([])
    setShoeUsed(0)
    setShoeRemaining(416)
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
    setPlayerPairStake(round.playerPairStake ?? 0)
    setBankerPairStake(round.bankerPairStake ?? 0)
    setBetSide(round.betSide)
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      setRevealedCards(round.playerCards.length + round.bankerCards.length)
      setRevealFinished(true)
      return
    }
    const timers = Array.from({ length: round.playerCards.length + round.bankerCards.length }, (_, index) =>
      window.setTimeout(() => setRevealedCards(index + 1), 160 + index * 220))
    timers.push(window.setTimeout(() => setRevealFinished(true), 160 + (timers.length - 1) * 220 + 280))
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
    }
    if (alreadyRecorded) return
    recordedRounds.current.add(round.roundId)
    const next = [{ roundId: round.roundId, outcome: round.outcome, cardsUsed, natural: round.endedOnNatural }, ...historyRef.current].slice(0, 120)
    historyRef.current = next
    setHistory(next)
  }, [completed, round, onBalanceChange, playerId])
  const balance = round?.balance ?? lastBalance ?? status?.balance ?? null
  const displayedBalance = lastBalance ?? status?.balance ?? null
  const totalStake = round?.totalStake ?? centsToMoney(moneyToCents(Number.isFinite(stake) ? stake : 0) + moneyToCents(playerPairStake) + moneyToCents(bankerPairStake))
  const stakeIsValid = status !== null && stake >= status.minimumStake && stake <= status.maximumStake &&
    isIncrementAligned(stake, status.minimumStake, status.stakeIncrement) &&
    (status.supportsPairBets === true || playerPairStake === 0 && bankerPairStake === 0) &&
    [playerPairStake, bankerPairStake].every(pairStake => pairStake === 0 || pairStake >= status.minimumStake && pairStake <= status.maximumStake && isIncrementAligned(pairStake, status.minimumStake, status.stakeIncrement))
  const stakeAffordable = balance !== null && totalStake <= balance
  const visibleError = error ?? statusError
  const stakeLocked = busy || round !== null || recovery !== 'ready' || !status?.available || pendingBet.current !== null

  const addChip = (side: BaccaratBetSide | 'player-pair' | 'banker-pair') => {
    if (stakeLocked || !status || balance === null) return
    if ((side === 'player-pair' || side === 'banker-pair') && status.supportsPairBets !== true) return
    const chipCents = moneyToCents(selectedChipValue)
    const mainCents = moneyToCents(Number.isFinite(stake) ? stake : 0)
    const playerPairCents = moneyToCents(playerPairStake)
    const bankerPairCents = moneyToCents(bankerPairStake)
    const maxCents = moneyToCents(status.maximumStake)
    if (side === 'player-pair') {
      if (playerPairCents + chipCents > maxCents || mainCents + playerPairCents + bankerPairCents + chipCents > moneyToCents(balance)) return
      setPlayerPairStake(centsToMoney(playerPairCents + chipCents))
      setPlacedChips(current => ({ ...current, playerPair: [...reconcileChipStack(current.playerPair, playerPairStake, status.maximumStake), selectedChipValue] }))
    } else if (side === 'banker-pair') {
      if (bankerPairCents + chipCents > maxCents || mainCents + playerPairCents + bankerPairCents + chipCents > moneyToCents(balance)) return
      setBankerPairStake(centsToMoney(bankerPairCents + chipCents))
      setPlacedChips(current => ({ ...current, bankerPair: [...reconcileChipStack(current.bankerPair, bankerPairStake, status.maximumStake), selectedChipValue] }))
    } else {
      const nextMainCents = (betSide === side ? mainCents : 0) + chipCents
      if (nextMainCents > maxCents || nextMainCents + playerPairCents + bankerPairCents > moneyToCents(balance)) return
      setBetSide(side)
      setStake(centsToMoney(nextMainCents))
      setPlacedChips(current => ({ ...current, main: betSide === side
        ? [...reconcileChipStack(current.main, stake, status.maximumStake), selectedChipValue]
        : [selectedChipValue] }))
    }
    playBaccaratChip(muted)
  }
  const clearWagers = () => { if (stakeLocked) return; setStake(0); setPlayerPairStake(0); setBankerPairStake(0); setPlacedChips(emptyPlacedChips()) }

  const act = async (operation: () => Promise<void>) => {
    setBusy(true); setError(null)
    try { await operation() } catch (reason) {
      if (isRejectedDeal(reason)) {
        clearPendingDeal(playerId)
        pendingBet.current = null
        dealRequestKey.current = null
        setLastBalance(null); setStatus(null)
        setStatusLoadAttempt(attempt => attempt + 1)
      }
      setError(messageForError(reason))
    } finally { setBusy(false) }
  }
  const submitDeal = (bet: Readonly<{ side: BaccaratBetSide; stake: number; playerPairStake: number; bankerPairStake: number }>, idempotencyKey: string) => {
    pendingBet.current = bet
    storePendingDeal(playerId, { idempotencyKey, betSide: bet.side, stake: bet.stake, playerPairStake: bet.playerPairStake, bankerPairStake: bet.bankerPairStake, placedChips })
    void act(async () => {
      const nextRound = await gateway.createRound(bet.side, bet.stake, { idempotencyKey, playerPairStake: bet.playerPairStake, bankerPairStake: bet.bankerPairStake })
      dealRequestKey.current = null
      pendingBet.current = null
      setRound(nextRound)
    })
  }
  const deal = () => {
    if (!status?.available || busy || recovery !== 'ready' || (!pendingBet.current && (!stakeIsValid || !stakeAffordable))) return
    const idempotencyKey = dealRequestKey.current ??= createRequestKey()
    submitDeal(pendingBet.current ?? { side: betSide, stake, playerPairStake, bankerPairStake }, idempotencyKey)
  }
  const closeResult = () => {
    if (!round || !completed || busy || recovery !== 'ready') return
    setLastBalance(round.balance)
    setActivePanel(null)
    setRound(null); setError(null); setBetSide('player'); setStake(0); setPlayerPairStake(0); setBankerPairStake(0)
    setPlacedChips(emptyPlacedChips())
    window.requestAnimationFrame(() => gameRef.current?.querySelector<HTMLButtonElement>('.ff-baccarat__chips button[aria-pressed="true"]')?.focus())
  }
  useEffect(() => {
    if (!completed || activePanel !== null) return
    const dismissOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      closeResult()
    }
    const dismissOnOutside = (event: PointerEvent) => {
      const result = gameRef.current?.querySelector('.ff-baccarat__result')
      if (event.target instanceof Node && !result?.contains(event.target)) closeResult()
    }
    document.addEventListener('keydown', dismissOnEscape)
    document.addEventListener('pointerdown', dismissOnOutside)
    return () => {
      document.removeEventListener('keydown', dismissOnEscape)
      document.removeEventListener('pointerdown', dismissOnOutside)
    }
  }, [completed, activePanel, round, busy, recovery])
  const retryStatus = () => { setStatusError(null); setTableUnavailable(false); setStatusLoadAttempt(attempt => attempt + 1) }
  const retryRestoration = () => { setError(null); setRecoveryAttempt(attempt => attempt + 1) }
  const road = <BaccaratRoad history={history} shoeUsed={shoeUsed} shoeRemaining={shoeRemaining} shoeVerified={shoeVerified} />

  return <main className="ff-baccarat" ref={gameRef} data-embedded={!showTitle} aria-busy={busy || recovery === 'recovering' || (round !== null && !completed)}>
    {!status && !statusError && <span className="ff-baccarat__sr-only" role="status">Loading Baccarat table…</span>}
    <header className="ff-baccarat__header">
      {showTitle && <h1>Baccarat</h1>}
      <div className="ff-baccarat__account"><span>Balance</span><strong>{displayedBalance === null ? '—' : formatMoney(displayedBalance, currencySymbol)}</strong></div>
      <InfoPanel label="Baccarat road and shoe" trigger="Road" activePanel={activePanel} onPanelChange={setActivePanel}>{road}</InfoPanel>
      <InfoPanel label="How to play Baccarat" trigger="?" activePanel={activePanel} onPanelChange={setActivePanel}>
        <h2>How to play</h2>
        <p>Choose Player, Banker or Tie. The closer hand to 9 wins. Cards deal automatically under standard Punto Banco rules; a natural 8 or 9 ends the hand.</p>
        <p>Ace counts 1, numbered cards count their value, and tens/court cards count 0. Only the last digit of a hand's total counts.</p>
        <p>Player pays 1:1. Banker pays 0.95:1 after 5% commission. Tie pays 8:1; a tie returns Player and Banker stakes. Player Pair and Banker Pair are separate 11:1 bets on matching ranks in the first two cards.</p>
        {status && <p>Stake {formatMoney(status.minimumStake, currencySymbol)}–{formatMoney(status.maximumStake, currencySymbol)}, in {formatMoney(status.stakeIncrement, currencySymbol)} increments.</p>}
        <p>The roads summarize completed hands. Session shoe estimates use cards shown on this device until server shoe data arrives.</p>
        <p>Blue marks Player, red marks Banker and green marks Tie on the Bead plate and Big Road. Big Eye Boy, Small Road and Cockroach Pig describe streak patterns; their colors do not identify a winning side or predict the next hand.</p>
      </InfoPanel>
      <button className="ff-baccarat__sound" type="button" aria-label={muted ? 'Turn sound on' : 'Mute sound'} aria-pressed={!muted} onClick={() => { const next = !muted; setMuted(next); writeBaccaratMuted(next) }}><SoundIcon muted={muted} /></button>
    </header>
    <section className="ff-baccarat__table" data-bet-side={betSide} aria-label="Baccarat table">
      <div className="ff-baccarat__inline-road" role="region" aria-label="Recent Baccarat history" tabIndex={0}>{road}</div>
      <div className="ff-baccarat__play-surface" ref={playSurfaceRef}>
      <div className="ff-baccarat__table-props" aria-hidden="true">
        <BankerChipRack />
      </div>
      <svg className="ff-baccarat__felt-lines" viewBox="0 0 1440 915" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
        <path d="M 0 330 Q 720 660 1440 330 M 0 410 Q 720 738 1440 410 M 0 494 Q 720 822 1440 494 M 0 593 Q 720 921 1440 593" />
        <path d="M 720 92 L 720 395 M 720 494 L 720 574 M 520 482 L 460 846 M 920 482 L 980 846" />
      </svg>
      <div className="ff-baccarat__chips" role="group" aria-label="Chip value">
        <button className="ff-baccarat__chip-arrow" type="button" aria-label="Show lower chip values" disabled={visibleChipWindowStart === 0} onClick={() => setChipWindowStart(start => Math.max(0, Math.min(start, maxChipWindowStart) - 1))}><svg viewBox="0 0 48 30" aria-hidden="true"><path d="M5 25 24 6 43 25" /></svg></button>
        <div className="ff-baccarat__chip-pill">
          {chipOptions.slice(visibleChipWindowStart, visibleChipWindowStart + visibleChipCount).map(chip => <button key={chip.value} type="button" aria-label={'Select ' + formatMoney(chip.value, currencySymbol) + ' chip'} aria-pressed={selectedChipValue === chip.value} disabled={stakeLocked || !status || chip.value > status.maximumStake} onClick={() => setSelectedChipValue(chip.value)}><img src={chip.image} alt="" draggable={false} style={{ transform: `scale(${chip.scale})` }} /></button>)}
        </div>
        <button className="ff-baccarat__chip-arrow" type="button" aria-label="Show higher chip values" disabled={visibleChipWindowStart >= maxChipWindowStart} onClick={() => setChipWindowStart(start => Math.min(maxChipWindowStart, Math.min(start, maxChipWindowStart) + 1))}><svg viewBox="0 0 48 30" aria-hidden="true"><path d="M5 5 24 24 43 5" /></svg></button>
      </div>
      <div className="ff-baccarat__hands">
        <Hand label="Player" cards={round?.playerCards ?? []} count={dealOrder.slice(0, revealedCards).filter(card => card.side === 'player').length} total={completed ? round.playerTotal : null} winner={completed && (round.outcome === 'player' || round.outcome === 'tie')} renderCardFace={renderCardFace} />
        <Hand label="Banker" cards={round?.bankerCards ?? []} count={dealOrder.slice(0, revealedCards).filter(card => card.side === 'banker').length} total={completed ? round.bankerTotal : null} winner={completed && (round.outcome === 'banker' || round.outcome === 'tie')} renderCardFace={renderCardFace} />
      </div>
      <div className="ff-baccarat__bet-sides" role="group" aria-label="Baccarat betting areas">
        <div className="ff-baccarat__bet-cell is-player-pair"><BetButton side="player-pair" selected={playerPairStake > 0} label="Player Pair" payout="11:1" disabled={stakeLocked || status?.supportsPairBets !== true} onSelect={() => addChip('player-pair')} stake={playerPairStake} placedChips={placedChips.playerPair} maxStake={status?.maximumStake ?? 0} currencySymbol={currencySymbol} winner={completed && round?.playerPairDisposition === 'win'} /></div>
        <div className="ff-baccarat__bet-cell is-banker-pair"><BetButton side="banker-pair" selected={bankerPairStake > 0} label="Banker Pair" payout="11:1" disabled={stakeLocked || status?.supportsPairBets !== true} onSelect={() => addChip('banker-pair')} stake={bankerPairStake} placedChips={placedChips.bankerPair} maxStake={status?.maximumStake ?? 0} currencySymbol={currencySymbol} winner={completed && round?.bankerPairDisposition === 'win'} /></div>
        <div className="ff-baccarat__bet-cell is-tie"><BetButton side="tie" selected={betSide === 'tie' && stake > 0} label="Tie" payout="8:1" disabled={stakeLocked} onSelect={() => addChip('tie')} stake={betSide === 'tie' ? stake : 0} placedChips={betSide === 'tie' ? placedChips.main : []} maxStake={status?.maximumStake ?? 0} currencySymbol={currencySymbol} winner={completed && round?.outcome === 'tie'} /></div>
        <div className="ff-baccarat__bet-cell is-banker"><BetButton side="banker" selected={betSide === 'banker' && stake > 0} label="Banker" payout="0.95:1" disabled={stakeLocked} onSelect={() => addChip('banker')} stake={betSide === 'banker' ? stake : 0} placedChips={betSide === 'banker' ? placedChips.main : []} maxStake={status?.maximumStake ?? 0} currencySymbol={currencySymbol} winner={completed && round?.outcome === 'banker'} /></div>
        <div className="ff-baccarat__bet-cell is-player"><BetButton side="player" selected={betSide === 'player' && stake > 0} label="Player" payout="1:1" disabled={stakeLocked} onSelect={() => addChip('player')} stake={betSide === 'player' ? stake : 0} placedChips={betSide === 'player' ? placedChips.main : []} maxStake={status?.maximumStake ?? 0} currencySymbol={currencySymbol} winner={completed && round?.outcome === 'player'} /></div>
      </div>
      {completed && round && <div className="ff-baccarat__result-layer" onClick={event => { if (event.target === event.currentTarget) closeResult() }}>
        <section className="ff-baccarat__result" role="dialog" aria-modal="false" aria-label="Baccarat result" onClick={event => event.stopPropagation()}>
          {round.endedOnNatural && <span className="ff-baccarat__result-natural">Natural</span>}
          <h2>{outcomeLabel(round.outcome)}</h2>
          <div className="ff-baccarat__result-amounts"><span>Net <strong>{formatSignedMoney(round.combinedProfit ?? round.profit, currencySymbol)}</strong></span><span>Returned <strong>{formatMoney(round.combinedTotalReturn ?? round.totalReturn, currencySymbol)}</strong></span></div>
          <button type="button" onClick={closeResult} autoFocus>Continue</button>
        </section>
      </div>}
      <svg className="ff-baccarat__table-edge" viewBox="0 0 1440 915" preserveAspectRatio="xMidYMid slice" aria-hidden="true"><defs>
        <pattern id="baccarat-black-leather" patternUnits="userSpaceOnUse" width="360" height="240"><image href={blackLeather} width="360" height="240" preserveAspectRatio="xMidYMid slice"/></pattern>
        <linearGradient id="baccarat-leather-shade" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#030505" stopOpacity=".15"/><stop offset=".45" stopColor="#000" stopOpacity=".4"/><stop offset="1" stopColor="#020403" stopOpacity=".75"/></linearGradient>
        <filter id="baccarat-rail-glint"><feGaussianBlur stdDeviation="3"/></filter>
      </defs>
        <path d="M 0 690 Q 720 1050 1440 690 L 1440 915 H 0 Z" fill="url(#baccarat-black-leather)"/>
        <path d="M 0 690 Q 720 1050 1440 690 L 1440 915 H 0 Z" fill="url(#baccarat-leather-shade)"/>
        <path d="M 0 690 Q 720 1050 1440 690" fill="none" stroke="#010805" strokeWidth="17" vectorEffect="non-scaling-stroke"/>
        <path d="M 0 697 Q 720 1057 1440 697" fill="none" stroke="#83694a" strokeOpacity=".65" strokeWidth="5" vectorEffect="non-scaling-stroke"/>
        <path d="M 0 704 Q 720 1064 1440 704" fill="none" stroke="#d6d3c9" strokeOpacity=".67" strokeWidth="8" vectorEffect="non-scaling-stroke" filter="url(#baccarat-rail-glint)"/>
        <path d="M 0 704 Q 720 1064 1440 704" fill="none" stroke="#a9a69d" strokeOpacity=".6" strokeWidth="2" vectorEffect="non-scaling-stroke"/>
      </svg>
      </div>
      <div className="ff-baccarat__rail" aria-label="Baccarat controls">
        <div className="ff-baccarat__table-totals" aria-label="Table amounts"><span>Bet <strong>{formatMoney(Number.isFinite(totalStake) ? totalStake : 0, currencySymbol)}</strong></span><span>Balance <strong>{displayedBalance === null ? '—' : formatMoney(displayedBalance, currencySymbol)}</strong></span><span>Win <strong>{formatMoney(completed ? round.combinedTotalReturn ?? round.totalReturn : 0, currencySymbol)}</strong></span></div>
        {!round && (recovery !== 'ready' || (!status && statusError)) ? <div className="ff-baccarat__recovery">
          {recovery === 'recovering' && <span role="status">Restoring your pending Baccarat deal…</span>}
          {recovery === 'failed' && <button className="ff-baccarat__primary" type="button" aria-label="Retry restoration" onClick={retryRestoration}>Retry restoration</button>}
          {!status && statusError && !tableUnavailable && <button className="ff-baccarat__primary" type="button" aria-label="Retry connection" onClick={retryStatus}>Retry connection</button>}
        </div> : !round ? <>
          <div className="ff-baccarat__wager-row">
            <button className="ff-baccarat__clear-wagers" type="button" aria-label="Clear Baccarat bets" disabled={stakeLocked || Number.isFinite(stake) && totalStake === 0} onClick={clearWagers}>×</button>
            <button className="ff-baccarat__primary ff-baccarat__icon-action" aria-label={busy ? 'Dealing…' : pendingBet.current ? 'Retry Deal' : 'Deal'} disabled={busy || recovery !== 'ready' || !status?.available || (!pendingBet.current && (!stakeIsValid || !stakeAffordable))} onClick={deal}><CardStackIcon /></button>
          </div>
        </> : <>
          <span className="ff-baccarat__ticket">{capitalize(round.betSide)} · {formatMoney(round.stake, currencySymbol)}</span>
          {!completed && <button className="ff-baccarat__primary ff-baccarat__icon-action" aria-label="Dealing…" disabled><CardStackIcon /></button>}
        </>}
      </div>
    </section>
    {visibleError && <p className="ff-baccarat__error" role="alert">{visibleError}</p>}
  </main>
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

function BetButton({ side, selected, label, payout, disabled, onSelect, stake, placedChips, maxStake, currencySymbol, winner }: Readonly<{ side: BaccaratBetSide | 'player-pair' | 'banker-pair'; selected: boolean; label: string; payout: string; disabled: boolean; onSelect: () => void; stake: number; placedChips: readonly ChipValue[]; maxStake: number; currencySymbol: string; winner: boolean }>) {
  const chips = reconcileChipStack(placedChips, stake, maxStake).map(value => chipOptions.find(chip => chip.value === value)!)
  const isPair = side === 'player-pair' || side === 'banker-pair'
  const displayedChips = isPair ? chips.slice(-3) : chips
  return <button type="button" className={'ff-baccarat__bet is-' + side + (selected ? ' is-selected' : '') + (winner ? ' is-winner' : '')} aria-label={label + ', ' + payout + ', ' + formatMoney(stake, currencySymbol)} aria-pressed={selected} disabled={disabled} onClick={onSelect}><strong>{label}</strong><span>{payout}</span>{(chips.length > 0 || isPair) && <span className="ff-baccarat__placed-chip" aria-hidden="true"><span className="ff-baccarat__chip-pile">{displayedChips.map((chip, index) => <img src={chip.image} alt="" draggable={false} style={{ bottom: `${index * 0.13}rem`, transform: `scale(${chip.scale})` }} key={index} />)}</span>{chips.length > 0 && <em>{formatMoney(stake, currencySymbol)}</em>}</span>}</button>
}
function visibleChips(stake: number, maxStake: number) {
  if (!Number.isFinite(stake) || stake < .25 || stake > maxStake || moneyToCents(stake) % 25 !== 0) return []
  let remainder = moneyToCents(stake)
  const chips: (typeof chipOptions)[number][] = []
  for (const chip of [...chipOptions].reverse()) {
    const chipCents = moneyToCents(chip.value)
    while (remainder >= chipCents) { chips.push(chip); remainder -= chipCents }
  }
  return chips
}
function reconcileChipStack(placed: readonly ChipValue[], stake: number, maxStake: number): readonly ChipValue[] {
  if (placed.length && placed.reduce((total, chip) => total + moneyToCents(chip), 0) === moneyToCents(stake)) return placed
  // A restored round may have no click history; infer only that baseline.
  return visibleChips(stake, maxStake).map(chip => chip.value)
}
function CardStackIcon() {
  return <svg className="ff-baccarat__action-icon" viewBox="0 0 64 64" aria-hidden="true" focusable="false"><rect x="12" y="11" width="38" height="43" rx="4" fill="#123d29" stroke="#edf2e4" strokeWidth="3" transform="rotate(-11 31 32)"/><rect x="17" y="11" width="38" height="43" rx="4" fill="#f4f5eb" stroke="#52715b" strokeWidth="2.5"/><path d="M36 18l12 15-12 15-12-15z" fill="#879b7c"/><path d="M36 24l7 9-7 9-7-9z" fill="#edf1e2"/></svg>
}

function Hand({ label, cards, count, total, winner, renderCardFace }: Readonly<{ label: string; cards: readonly BaccaratCard[]; count: number; total: number | null; winner: boolean; renderCardFace?: BaccaratGameProps['renderCardFace'] }>) {
  return <section className={'ff-baccarat__hand' + (winner ? ' is-winner' : '')} aria-label={label + ' hand' + (total === null ? '' : ', total ' + total)}>
    <div className="ff-baccarat__hand-title"><span>{label}</span><strong aria-label={total === null ? label + ' total pending' : label + ' total ' + total}>{total === null ? null : total}</strong></div>
    <div className="ff-baccarat__cards">{[0, 1, 2].map(index => <div key={index} className="ff-baccarat__card-slot">
      {index < count && cards[index] ? renderCardFace ? <div className="ff-baccarat__shared-card-face">{renderCardFace(cards[index])}</div> : <CardFace card={cards[index]} /> : cards.length > 0 && index < 2 ? <div className="ff-baccarat__card ff-baccarat__card--back" aria-hidden="true">FF</div> : null}
    </div>)}</div>
  </section>
}
function BaccaratRoad({ history, shoeUsed, shoeRemaining, shoeVerified }: Readonly<{ history: readonly BaccaratHistoryItem[]; shoeUsed: number; shoeRemaining: number; shoeVerified: boolean }>) {
  const player = history.filter(item => item.outcome === 'player').length
  const banker = history.filter(item => item.outcome === 'banker').length
  const ties = history.filter(item => item.outcome === 'tie').length
  const bigRoad = buildBigRoad(history)
  const derivedRoads = buildDerivedRoads(history)
  return <section className="ff-baccarat__road" aria-label="Your recent Baccarat hands, roads, and shoe progress">
    <h2>Roadmap</h2>
    <div className="ff-baccarat__road-stats"><span>Banker <b>{banker}</b></span><span>Player <b>{player}</b></span><span>Tie <b>{ties}</b></span><span>Total <b>{history.length}</b></span></div>
    <div className="ff-baccarat__beads"><small>Bead plate</small><ol aria-label="Recent outcomes, oldest to newest down each column">{history.slice(0, 60).reverse().map(item => <li className={'is-' + item.outcome} title={capitalize(item.outcome) + (item.natural ? ' natural' : '')} key={item.roundId}>{item.outcome[0]?.toUpperCase()}</li>)}</ol></div>
    <div className="ff-baccarat__big-road"><small>Big Road</small><div role="img" aria-label={'Recent Big Road, ' + bigRoad.length + ' marks shown. Stored history: ' + player + ' Player wins, ' + banker + ' Banker wins, and ' + ties + ' ties'}>{bigRoad.map(cell => <i className={'is-' + cell.outcome} style={{ gridColumn: cell.column + 1, gridRow: cell.row + 1 }} title={capitalize(cell.outcome) + (cell.ties ? ' with ' + cell.ties + ' ties' : '')} key={cell.column + '-' + cell.row}><span>{cell.ties || ''}</span></i>)}</div></div>
    <div className="ff-baccarat__derived-roads">
      {([['bigEyeBoy', 'Big Eye Boy'], ['smallRoad', 'Small Road'], ['cockroachPig', 'Cockroach Pig']] as const).map(([kind, label]) => <div className={'ff-baccarat__derived-road is-' + kind} key={kind}><small>{label}</small><div role="img" aria-label={label + ', recent pattern: ' + derivedRoads[kind].filter(cell => cell.color === 'red').length + ' red and ' + derivedRoads[kind].filter(cell => cell.color === 'blue').length + ' blue marks'}>{derivedRoads[kind].map(cell => <i className={'is-' + cell.color} style={{ gridColumn: cell.column + 1, gridRow: cell.row + 1 }} key={cell.column + '-' + cell.row} />)}</div></div>)}
    </div>
    <div className="ff-baccarat__shoe-progress"><span>{shoeVerified ? 'Live shoe' : 'Session estimate'}</span><strong>{shoeRemaining} cards</strong><i aria-hidden="true"><b style={{ width: Math.min(100, (shoeUsed / 312) * 100) + '%' }} /></i><small>{shoeVerified ? 'Server shoe reshuffles at 75% penetration.' : 'Tracks cards shown on this device until server shoe data arrives.'}</small></div>
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
function moneyToCents(value: number) { return Math.round(value * 100) }
function centsToMoney(value: number) { return value / 100 }
function shortRank(rank: BaccaratCard['rank']) { return ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[rank] }
function suitSymbol(suit: BaccaratCard['suit']) { return ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[suit] }
function outcomeLabel(outcome: BaccaratRound['outcome']) { return outcome === 'tie' ? 'Tie' : `${capitalize(outcome)} wins` }
function capitalize(value: string) { return value[0]?.toUpperCase() + value.slice(1) }
function formatMoney(value: number, currencySymbol: string) { return `${value < 0 ? '-' : ''}${currencySymbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function formatSignedMoney(value: number, currencySymbol: string) { return `${value > 0 ? '+' : ''}${formatMoney(value, currencySymbol)}` }
function messageForError(reason: unknown) { return reason instanceof BaccaratGatewayError ? reason.message : 'The Baccarat table is unavailable.' }
function isTableUnavailable(reason: unknown) { return reason instanceof BaccaratGatewayError && reason.code === 'baccarat-disabled' }
function isRejectedDeal(reason: unknown) {
  // These responses guarantee no wager was accepted. Ambiguous failures must
  // keep their original request key so retries cannot place another wager.
  return reason instanceof BaccaratGatewayError && (
    reason.code === 'baccarat-pair-unavailable' ||
    reason.status === 400 && reason.code === 'baccarat-invalid-request' ||
    reason.status === 409 && reason.code === 'insufficient-slot-credits'
  )
}
function createRequestKey() { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `baccarat-${random}` }
function pendingDealKey(playerId: string) { return `fortuneforge:baccarat:pending:${playerId}` }
function readPendingDeal(playerId: string): StoredPendingDeal | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingDealKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const deal = value as Record<string, unknown>
    if (typeof deal.idempotencyKey === 'string' && typeof deal.stake === 'number' && (deal.betSide === 'player' || deal.betSide === 'banker' || deal.betSide === 'tie')) {
      const playerPairStake = typeof deal.playerPairStake === 'number' ? deal.playerPairStake : 0
      const bankerPairStake = typeof deal.bankerPairStake === 'number' ? deal.bankerPairStake : 0
      return { idempotencyKey: deal.idempotencyKey, betSide: deal.betSide, stake: deal.stake,
        playerPairStake, bankerPairStake,
        placedChips: parsePlacedChips(deal.placedChips, deal.stake, playerPairStake, bankerPairStake) }
    }
  } catch { /* session storage is optional */ }
  return null
}
function parsePlacedChips(value: unknown, stake: number, playerPairStake: number, bankerPairStake: number): PlacedChips | undefined {
  if (!value || typeof value !== 'object') return undefined
  const stacks = value as Record<string, unknown>
  const isChipStack = (stack: unknown, expected: number): stack is ChipValue[] =>
    Array.isArray(stack) && stack.length <= 400 && stack.every(chip => chipOptions.some(option => option.value === chip)) &&
    stack.reduce((total, chip) => total + moneyToCents(chip as number), 0) === moneyToCents(expected)
  if (!isChipStack(stacks.main, stake) || !isChipStack(stacks.playerPair, playerPairStake) ||
    !isChipStack(stacks.bankerPair, bankerPairStake)) return undefined
  return { main: stacks.main, playerPair: stacks.playerPair, bankerPair: stacks.bankerPair }
}
function storePendingDeal(playerId: string | undefined, deal: StoredPendingDeal) { if (!playerId) return; try { sessionStorage.setItem(pendingDealKey(playerId), JSON.stringify(deal)) } catch { /* session storage is optional */ } }
function clearPendingDeal(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingDealKey(playerId)) } catch { /* session storage is optional */ } }
function clearStoredRoadmap(playerId: string | undefined) {
  // Remove history and shoe estimates written by older clients. They no longer
  // persist between visits; pending wagers use their own recovery key above.
  try {
    const suffix = playerId ?? 'guest'
    localStorage.removeItem(`fortuneforge:baccarat:history:${suffix}`)
    localStorage.removeItem(`fortuneforge:baccarat:shoe:${suffix}`)
  } catch { /* storage may be unavailable */ }
}
function SoundIcon({ muted }: Readonly<{ muted: boolean }>) {
  return <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M3 9h4l5-4v14l-5-4H3z" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" />{muted ? <path d="m16 9 5 6m0-6-5 6" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" /> : <><path d="M15 8q4 4 0 8M18 5q7 7 0 14" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" /></>}</svg>
}

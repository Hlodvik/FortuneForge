import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { VideoPokerGatewayError, type VideoPokerCard, type VideoPokerCardPosition, type VideoPokerGateway, type VideoPokerHandCount, type VideoPokerRound, type VideoPokerStatus } from './contracts'
import { HttpVideoPokerGateway } from './httpVideoPokerGateway'
import { cardName, formatMoney, handLabel, paytableRows, recommendedHolds } from './videoPokerPresentation'
import './videoPoker.css'
import './videoPokerPaytable.css'
import './videoPokerEnhancements.css'
import './videoPokerViewport.css'

export type VideoPokerGameProps = Readonly<{ gateway?: VideoPokerGateway; playerId?: string; currencySymbol?: string; onBalanceChange?: (balance: number) => void; showTitle?: boolean }>
const defaultGateway = new HttpVideoPokerGateway()
type StoredPendingAction =
  | Readonly<{ operation: 'deal'; idempotencyKey: string; coinsWagered: number; handCount: VideoPokerHandCount }>
  | Readonly<{ operation: 'draw'; idempotencyKey: string; roundId: string; heldPositions: readonly VideoPokerCardPosition[] }>

export function VideoPokerGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange, showTitle = true }: VideoPokerGameProps) {
  const [status, setStatus] = useState<VideoPokerStatus | null>(null)
  const [round, setRound] = useState<VideoPokerRound | null>(null)
  const [coinsWagered, setCoinsWagered] = useState(1)
  const [handCount, setHandCount] = useState<VideoPokerHandCount>(1)
  const [heldPositions, setHeldPositions] = useState<readonly VideoPokerCardPosition[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [strategyHelp, setStrategyHelp] = useState(false)
  const [activePanel, setActivePanel] = useState<string | null>(null)
  const [revealState, setRevealState] = useState({ key: '', count: 0, finished: false })
  const [lastBalance, setLastBalance] = useState<number | null>(null)
  const dealRequestKey = useRef<string | null>(null)
  const drawRequestKey = useRef<string | null>(null)
  const pendingDeal = useRef<Readonly<{ coins: number; hands: VideoPokerHandCount }> | null>(null)
  const pendingDraw = useRef<readonly VideoPokerCardPosition[] | null>(null)
  const statusRefreshBalance = useRef(false)
  const balanceCallback = useRef(onBalanceChange)
  const gameRef = useRef<HTMLElement>(null)
  useEffect(() => { balanceCallback.current = onBalanceChange }, [onBalanceChange])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(next => {
      if (controller.signal.aborted) return
      setStatus(next)
      if (statusLoadAttempt > 0 && statusRefreshBalance.current) { statusRefreshBalance.current = false; balanceCallback.current?.(next.balance) }
      setCoinsWagered(current => current >= next.minimumCoinsWagered && current <= next.maximumCoinsWagered ? current : next.minimumCoinsWagered)
      setHandCount(current => (next.handCounts ?? [1, 3, 5]).includes(current) ? current : next.handCounts?.[0] ?? 1)
      setTableUnavailable(!next.available)
      setStatusError(next.available ? null : 'This table is temporarily unavailable. Please choose another game.')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      const unavailable = isTableUnavailable(reason)
      setTableUnavailable(unavailable)
      setStatusError(unavailable ? 'This table is temporarily unavailable. Please choose another game.' : messageForError(reason))
    })
    return () => controller.abort()
  }, [gateway, statusLoadAttempt])

  const releaseRejectedDeal = () => {
    statusRefreshBalance.current = true
    dealRequestKey.current = null; pendingDeal.current = null; clearPendingAction(playerId)
    setStatus(null); setStatusError(null); setLastBalance(null); setStatusLoadAttempt(attempt => attempt + 1)
  }
  const acceptRound = (next: VideoPokerRound) => {
    setRound(next); setCoinsWagered(next.coinsWagered); setHandCount(next.handCount)
    setHeldPositions(next.phase === 'awaiting-draw' ? readHolds(playerId, next.roundId) ?? next.heldPositions : next.heldPositions)
    if (next.phase === 'awaiting-draw') storeRoundId(playerId, next.roundId)
  }
  useEffect(() => {
    const pending = playerId ? readPendingAction(playerId) : null
    const id = playerId ? readStoredRoundId(playerId) : null
    if (!pending && !id) { setRecovery('ready'); return }
    const controller = new AbortController()
    setRecovery('recovering')
    const request = pending?.operation === 'deal'
      ? gateway.createRound(pending.coinsWagered, { signal: controller.signal, idempotencyKey: pending.idempotencyKey, handCount: pending.handCount })
      : pending?.operation === 'draw'
        ? gateway.draw(pending.roundId, pending.heldPositions, { signal: controller.signal, idempotencyKey: pending.idempotencyKey })
        : gateway.getRound(id!, controller.signal)
    void request.then(next => {
      if (controller.signal.aborted) return
      acceptRound(next); setRecovery('ready'); setError(null)
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      if (pending?.operation === 'deal' && isRejectedDeal(reason)) {
        releaseRejectedDeal(); setRecovery('ready'); setError(messageForError(reason)); return
      }
      if (!pending && reason instanceof VideoPokerGatewayError && reason.status === 404) {
        clearStoredRoundId(playerId); clearHolds(playerId); setRecovery('ready'); setError(null); return
      }
      setRecovery('failed')
      setError(pending ? 'Your pending Video Poker request could not be restored. Retry restoration before starting another deal.' : 'Your unfinished hand could not be restored. Retry restoration before starting another deal.')
    })
    return () => controller.abort()
  }, [gateway, playerId, recoveryAttempt])

  const revealKey = round ? round.roundId + ':' + round.phase : ''
  const revealed = revealState.key === revealKey ? revealState.count : 0
  const revealFinished = revealState.key === revealKey && revealState.finished
  const drawPositions = round?.phase === 'completed' ? [0, 1, 2, 3, 4].filter(position => !round.heldPositions.includes(position as VideoPokerCardPosition)) : [0, 1, 2, 3, 4]
  useEffect(() => {
    setRevealState({ key: revealKey, count: 0, finished: false })
    if (!round) return
    const positions = round.phase === 'completed' ? [0, 1, 2, 3, 4].filter(position => !round.heldPositions.includes(position as VideoPokerCardPosition)) : [0, 1, 2, 3, 4]
    const timers = positions.map((_, index) => window.setTimeout(() => setRevealState({ key: revealKey, count: index + 1, finished: false }), 100 + index * 90))
    const entrance = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 0 : 160
    timers.push(window.setTimeout(() => setRevealState({ key: revealKey, count: positions.length, finished: true }), positions.length ? 100 + (positions.length - 1) * 90 + entrance : 20))
    return () => timers.forEach(timer => window.clearTimeout(timer))
  }, [revealKey])
  const awaitingDraw = round?.phase === 'awaiting-draw' && revealFinished
  const completed = round?.phase === 'completed' && revealFinished
  useEffect(() => {
    if (!round || !revealFinished) return
    clearPendingAction(playerId)
    if (round.phase === 'completed') { clearStoredRoundId(playerId); clearHolds(playerId) }
    setLastBalance(round.balance)
    balanceCallback.current?.(round.balance)
    if (document.activeElement === document.body) gameRef.current?.querySelector<HTMLButtonElement>('.ff-video-poker__card-button:not(:disabled), .ff-video-poker__rail button:not(:disabled)')?.focus()
  }, [round, revealFinished, playerId])

  const toggleHold = (position: VideoPokerCardPosition) => {
    if (!round || !awaitingDraw || busy || recovery !== 'ready' || pendingDraw.current) return
    const next = heldPositions.includes(position) ? heldPositions.filter(value => value !== position) : [...heldPositions, position].sort() as VideoPokerCardPosition[]
    setHeldPositions(next); storeHolds(playerId, round.roundId, next)
  }
  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      if (activePanel || event.repeat || event.ctrlKey || event.metaKey || event.altKey || !/^[1-5]$/.test(event.key)) return
      if (!gameRef.current?.contains(document.activeElement) || /^(INPUT|SELECT|TEXTAREA)$/.test(document.activeElement?.tagName ?? '')) return
      if (!awaitingDraw || busy || recovery !== 'ready' || pendingDraw.current) return
      event.preventDefault(); toggleHold((Number(event.key) - 1) as VideoPokerCardPosition)
    }
    document.addEventListener('keydown', shortcut)
    return () => document.removeEventListener('keydown', shortcut)
  }, [awaitingDraw, busy, recovery, heldPositions, activePanel])

  const balance = round?.balance ?? lastBalance ?? status?.balance ?? null
  const displayedBalance = lastBalance ?? status?.balance ?? null
  const counts = status?.handCounts ?? [1, 3, 5]
  const wagerValid = status !== null && Number.isInteger(coinsWagered) && coinsWagered >= status.minimumCoinsWagered && coinsWagered <= status.maximumCoinsWagered && counts.includes(handCount)
  const affordable = balance !== null && status !== null && coinsWagered * handCount * status.coinValue <= balance
  const maxAffordable = balance !== null && status !== null && status.maximumCoinsWagered * handCount * status.coinValue <= balance
  const stakeLocked = busy || recovery !== 'ready' || !status?.available || pendingDeal.current !== null
  const act = async (operation: () => Promise<void>) => {
    setBusy(true); setError(null)
    try { await operation() } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }
  const deal = (coinOverride = coinsWagered, handOverride = handCount) => {
    if (!status?.available || busy || recovery !== 'ready' || (round && !completed)) return
    const bet = pendingDeal.current ?? { coins: coinOverride, hands: handOverride }
    if (!pendingDeal.current && (!counts.includes(bet.hands) || bet.coins < status.minimumCoinsWagered || bet.coins > status.maximumCoinsWagered || !Number.isInteger(bet.coins) || balance === null || bet.coins * bet.hands * status.coinValue > balance)) return
    const key = dealRequestKey.current ??= createRequestKey('video-poker-deal')
    pendingDeal.current = bet
    setCoinsWagered(bet.coins); setHandCount(bet.hands); setActivePanel(null); setRound(null)
    storePendingAction(playerId, { operation: 'deal', idempotencyKey: key, coinsWagered: bet.coins, handCount: bet.hands })
    void act(async () => {
      try {
        const next = await gateway.createRound(bet.coins, { idempotencyKey: key, handCount: bet.hands })
        dealRequestKey.current = null; pendingDeal.current = null; acceptRound(next)
      } catch (reason) {
        if (isRejectedDeal(reason)) releaseRejectedDeal()
        throw reason
      }
    })
  }
  const draw = () => {
    if (!round || !awaitingDraw || busy || recovery !== 'ready') return
    const positions = pendingDraw.current ?? heldPositions
    const key = drawRequestKey.current ??= createRequestKey('video-poker-draw')
    pendingDraw.current = positions
    setActivePanel(null)
    storePendingAction(playerId, { operation: 'draw', idempotencyKey: key, roundId: round.roundId, heldPositions: positions })
    void act(async () => {
      const next = await gateway.draw(round.roundId, positions, { idempotencyKey: key })
      drawRequestKey.current = null; pendingDraw.current = null; acceptRound(next)
    })
  }
  const newHand = () => {
    if (!round || !completed || busy || recovery !== 'ready') return
    setLastBalance(round.balance); setCoinsWagered(status ? Math.min(status.maximumCoinsWagered, Math.max(status.minimumCoinsWagered, round.coinsWagered)) : round.coinsWagered)
    setHandCount(counts.includes(round.handCount) ? round.handCount : counts[0]!)
    setRound(null); setHeldPositions([]); setError(null); setActivePanel(null)
    window.requestAnimationFrame(() => gameRef.current?.querySelector<HTMLElement>('select:not(:disabled), .ff-video-poker__rail button:not(:disabled)')?.focus())
  }
  const retryStatus = () => { setStatusError(null); setTableUnavailable(false); setStatusLoadAttempt(attempt => attempt + 1) }
  const retryRestoration = () => { setError(null); setRecoveryAttempt(attempt => attempt + 1) }
  const shownCoins = round?.coinsWagered ?? coinsWagered
  const paytable = <Paytable coins={shownCoins} round={completed ? round : null} />
  const visibleError = error ?? statusError

  return <main className="ff-video-poker" ref={gameRef} data-embedded={!showTitle} aria-busy={busy || recovery === 'recovering' || (round !== null && !revealFinished)}>
    <header className="ff-video-poker__header">
      {showTitle && <h1>Video Poker</h1>}
      <div className="ff-video-poker__balance"><span>Balance</span><strong>{displayedBalance === null ? '—' : formatMoney(displayedBalance, currencySymbol)}</strong></div>
      <InfoPanel label="Video Poker paytable" trigger="Paytable" active={activePanel} onChange={setActivePanel}>{paytable}</InfoPanel>
      <InfoPanel label="Video Poker basic guide" trigger="Guide" active={activePanel} onChange={setActivePanel}>
        <h2>Basic holds</h2><button className="ff-video-poker__secondary" aria-pressed={strategyHelp} onClick={() => setStrategyHelp(value => !value)}>Strategy {strategyHelp ? 'On' : 'Off'}</button>
        <p>This optional guide identifies made hands, pairs and high cards. It does not calculate optimal returns or select your holds.</p>
        {strategyHelp && awaitingDraw && <p role="status">{recommendedHolds(round.initialCards).length ? 'Consider holding ' + recommendedHolds(round.initialCards).map(position => cardName(round.initialCards[position])).join(', ') + '.' : 'No simple hold found.'}</p>}
      </InfoPanel>
      <InfoPanel label="How to play Video Poker" trigger="?" active={activePanel} onChange={setActivePanel}>
        <h2>Jacks or Better · 9/6</h2><p>Choose coins and hands, then Deal. Tap cards to hold them and Draw to replace the others. Use 1–5 while the cards are focused, or Tab and Space.</p>
        <p>Each hand shares the deal and holds, then draws from its own shuffled deck. A pair of jacks or higher pays; lower pairs do not. The paytable shows coins returned per hand, including the 4,000-coin Royal Flush at five coins.</p>
        {status && <p>One coin is {formatMoney(status.coinValue, currencySymbol)}. Total wager is coins × hands × coin value.</p>}
      </InfoPanel>
    </header>
    <section className="ff-video-poker__console" aria-label="Video Poker game">
      <div className="ff-video-poker__round-head" role="status">{completed ? <strong className={round.payout! > 0 ? 'is-win' : 'is-loss'}>{round.payout === 0 ? 'No win' : formatMoney(round.payout!, currencySymbol) + ' total won'}</strong> : awaitingDraw ? <span>{heldPositions.length} held</span> : round ? <span>{round.phase === 'completed' || busy ? 'Drawing…' : 'Dealing…'}</span> : null}</div>
      <div className="ff-video-poker__screen">
        <div className="ff-video-poker__inline-paytable">{paytable}</div>
        <div className="ff-video-poker__board" data-count={round?.phase === 'completed' ? round.handCount : 1} aria-label={round?.phase === 'completed' ? 'Your final hands' : 'Your shared dealt cards'}>
          {round?.phase === 'completed' ? round.finalHands?.map((cards, handIndex) => <section className={'ff-video-poker__completed-hand' + (completed && round.handPayouts![handIndex]! > 0 ? ' is-winning' : '')} key={handIndex}>
            <header><span>Hand {handIndex + 1}</span><strong>{completed ? handLabel(round.handRanks![handIndex]!, round.handPayouts![handIndex]!) : '—'}</strong>{round.handCount > 1 && <em>{completed ? formatMoney(round.handPayouts![handIndex]!, currencySymbol) : '—'}</em>}</header>
            <div className="ff-video-poker__cards">{cards.map((card, index) => <div className={'ff-video-poker__card-slot' + (round.heldPositions.includes(index as VideoPokerCardPosition) ? ' is-held' : '')} key={index}>
              {round.heldPositions.includes(index as VideoPokerCardPosition) || drawPositions.slice(0, revealed).includes(index) ? <CardFace card={card} animate={!round.heldPositions.includes(index as VideoPokerCardPosition)} /> : <CardBack />}
            </div>)}</div>
          </section>) : <section className="ff-video-poker__shared-hand">
            <div className="ff-video-poker__cards">{[0, 1, 2, 3, 4].map(position => <div className="ff-video-poker__card-slot" key={position}>
              {round && position < revealed ? <CardButton card={round.initialCards[position]!} position={position as VideoPokerCardPosition} held={heldPositions.includes(position as VideoPokerCardPosition)} disabled={!awaitingDraw || busy || recovery !== 'ready' || pendingDraw.current !== null} onToggle={toggleHold} /> : <CardBack />}
            </div>)}</div>
          </section>}
        </div>
      </div>
      <div className="ff-video-poker__rail">
        {!round && (recovery !== 'ready' || (!status && statusError)) ? <div className="ff-video-poker__recovery">
          {recovery === 'recovering' && <span role="status">Restoring your unfinished hand…</span>}
          {recovery === 'failed' && <button className="ff-video-poker__primary" onClick={retryRestoration}>Retry restoration</button>}
          {!status && statusError && !tableUnavailable && <button className="ff-video-poker__primary" onClick={retryStatus}>Retry connection</button>}
        </div> : !round ? <>
          <div className="ff-video-poker__settings">
            <label>Coin wager<select aria-label="Coin wager" value={coinsWagered} disabled={stakeLocked} onChange={event => setCoinsWagered(Number(event.target.value))}>
              {status && Array.from({ length: status.maximumCoinsWagered - status.minimumCoinsWagered + 1 }, (_, index) => status.minimumCoinsWagered + index).map(value => <option key={value} value={value}>{value} {value === 1 ? 'coin' : 'coins'} · {formatMoney(value * status.coinValue, currencySymbol)}</option>)}
            </select></label>
            <div className="ff-video-poker__hand-count" role="group" aria-label="Hands"><span>Hands</span><div>{counts.map(value => <button className="ff-video-poker__secondary" key={value} disabled={stakeLocked} aria-pressed={handCount === value} onClick={() => setHandCount(value)}>{value}</button>)}</div></div>
          </div>
          <div className="ff-video-poker__bet-controls"><button className="ff-video-poker__secondary" disabled={stakeLocked} onClick={() => status && setCoinsWagered(value => value >= status.maximumCoinsWagered ? status.minimumCoinsWagered : value + 1)}>Bet 1</button><button className="ff-video-poker__secondary" disabled={stakeLocked || !maxAffordable} onClick={() => status && deal(status.maximumCoinsWagered)}>Bet Max &amp; Deal</button><button className="ff-video-poker__primary" disabled={busy || recovery !== 'ready' || !status?.available || (!pendingDeal.current && (!wagerValid || !affordable))} onClick={() => deal()}>{busy ? 'Dealing…' : pendingDeal.current ? 'Retry Deal' : 'Deal'}</button></div>
          <small className="ff-video-poker__ticket">{status ? 'Wager ' + formatMoney(coinsWagered * handCount * status.coinValue, currencySymbol) : '—'}</small>
        </> : <>
          <small className="ff-video-poker__ticket">{round.coinsWagered} {round.coinsWagered === 1 ? 'coin' : 'coins'} × {round.handCount} {round.handCount === 1 ? 'hand' : 'hands'} · {formatMoney(round.wager, currencySymbol)}</small>
          <div className="ff-video-poker__round-actions">{round.phase === 'awaiting-draw' ? <button className="ff-video-poker__primary" disabled={busy || !awaitingDraw || recovery !== 'ready'} onClick={draw}>{busy ? 'Drawing…' : !awaitingDraw ? 'Dealing…' : pendingDraw.current ? 'Retry Draw' : 'Draw'}</button> : !completed ? <button className="ff-video-poker__primary" disabled>Drawing…</button> : <>
            {status?.available ? <button className="ff-video-poker__primary" disabled={busy || !counts.includes(round.handCount) || round.coinsWagered < status.minimumCoinsWagered || round.coinsWagered > status.maximumCoinsWagered || round.coinsWagered * round.handCount * status.coinValue > round.balance} onClick={() => deal(round.coinsWagered, round.handCount)}>Deal Again</button> : !status && statusError && !tableUnavailable ? <button className="ff-video-poker__primary" onClick={retryStatus}>Retry connection</button> : <button className="ff-video-poker__primary" disabled>Deal Again</button>}
            <button className="ff-video-poker__secondary" onClick={newHand}>New Hand</button>
            <InfoPanel label="Video Poker round details" trigger="Details" active={activePanel} onChange={setActivePanel}><h2>Round details</h2><dl><div><dt>Wager</dt><dd>{formatMoney(round.wager, currencySymbol)}</dd></div><div><dt>Total return</dt><dd>{formatMoney(round.payout!, currencySymbol)}</dd></div><div><dt>Net</dt><dd>{formatMoney(round.payout! - round.wager, currencySymbol)}</dd></div></dl></InfoPanel>
          </>}</div>
          {!status && statusError && !tableUnavailable && round.phase === 'awaiting-draw' && <button className="ff-video-poker__secondary" onClick={retryStatus}>Retry connection</button>}
        </>}
      </div>
    </section>
    {visibleError && <p className="ff-video-poker__error" role="alert">{visibleError}</p>}
  </main>
}

function CardButton({ card, position, held, disabled, onToggle }: Readonly<{ card: VideoPokerCard; position: VideoPokerCardPosition; held: boolean; disabled: boolean; onToggle: (position: VideoPokerCardPosition) => void }>) {
  return <button className={'ff-video-poker__card-button' + (held ? ' is-held' : '')} type="button" aria-pressed={held} aria-label={(held ? 'Release ' : 'Hold ') + cardName(card)} disabled={disabled} onClick={() => onToggle(position)}><CardFace card={card} animate /><span className="ff-video-poker__hold-label">{held ? 'HELD' : 'HOLD'}</span></button>
}
function CardFace({ card, animate = false }: Readonly<{ card: VideoPokerCard; animate?: boolean }>) {
  const symbol = ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[card.suit]
  const rank = ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[card.rank]
  return <div className={'ff-video-poker__card' + (['diamonds', 'hearts'].includes(card.suit) ? ' is-red' : '')} role="img" aria-label={cardName(card)} data-new={animate}><span className="ff-video-poker__corner">{rank}<small>{symbol}</small></span><span className="ff-video-poker__suit" aria-hidden="true">{symbol}</span><span className="ff-video-poker__corner ff-video-poker__corner--reverse">{rank}<small>{symbol}</small></span></div>
}
function CardBack() { return <div className="ff-video-poker__card ff-video-poker__card--back" aria-hidden="true">FF</div> }
function Paytable({ coins, round }: Readonly<{ coins: number; round: VideoPokerRound | null }>) {
  const wins = round?.handRanks?.filter((_, index) => round.handPayouts![index]! > 0).map(rank => handLabel(rank, 1)) ?? []
  return <section className="ff-video-poker__paytable"><h2>Jacks or Better <small>9/6 · {coins} {coins === 1 ? 'coin' : 'coins'}</small></h2><table aria-label="Jacks or Better paytable"><thead><tr><th scope="col">Hand</th><th scope="col">Coins returned</th></tr></thead><tbody>{paytableRows(coins).map(row => <tr className={wins.some(hand => hand === row.hand) ? 'is-winning-row' : ''} key={row.hand}><th scope="row">{row.hand}</th><td>{row.payout.toLocaleString()} {row.payout === 1 ? 'coin' : 'coins'}</td></tr>)}</tbody></table></section>
}
function InfoPanel({ label, trigger, children, active, onChange }: Readonly<{ label: string; trigger: string; children: ReactNode; active: string | null; onChange: (label: string | null) => void }>) {
  const open = active === label, id = useId()
  const container = useRef<HTMLDivElement>(null), button = useRef<HTMLButtonElement>(null), close = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    if (!open) return
    close.current?.focus()
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { onChange(null); if (container.current?.contains(document.activeElement)) button.current?.focus() } }
    const outside = (event: PointerEvent) => { if (!container.current?.contains(event.target as Node)) onChange(null) }
    document.addEventListener('keydown', escape); document.addEventListener('pointerdown', outside)
    return () => { document.removeEventListener('keydown', escape); document.removeEventListener('pointerdown', outside) }
  }, [open, onChange])
  return <div className="ff-video-poker__info" ref={container}><button className="ff-video-poker__secondary" type="button" aria-label={label} aria-expanded={open} aria-controls={id} ref={button} onClick={() => onChange(open ? null : label)}>{trigger}</button>{open && <section className="ff-video-poker__info-panel" role="dialog" aria-label={label} id={id}><button className="ff-video-poker__secondary" aria-label={'Close ' + label.toLowerCase()} ref={close} onClick={() => { onChange(null); button.current?.focus() }}>×</button>{children}</section>}</div>
}

function messageForError(reason: unknown) { return reason instanceof VideoPokerGatewayError ? reason.message : 'The Video Poker service is unavailable.' }
function isRejectedDeal(reason: unknown) {
  if (!(reason instanceof VideoPokerGatewayError)) return false
  return (reason.status === 409 && reason.code === 'insufficient-slot-credits') ||
    (reason.status === 400 && ['video-poker-invalid-request', 'video-poker-invalid-wager', 'video-poker-insufficient-balance'].includes(reason.code))
}
function isTableUnavailable(reason: unknown) { return reason instanceof VideoPokerGatewayError && reason.code === 'video-poker-disabled' }
function createRequestKey(prefix: string) { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `${prefix}-${random}` }
function storedRoundKey(playerId: string) { return `fortuneforge:video-poker:round:${playerId}` }
function readStoredRoundId(playerId: string) { try { return sessionStorage.getItem(storedRoundKey(playerId)) } catch { return null } }
function storeRoundId(playerId: string | undefined, roundId: string) { if (!playerId) return; try { sessionStorage.setItem(storedRoundKey(playerId), roundId) } catch { /* optional storage */ } }
function clearStoredRoundId(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(storedRoundKey(playerId)) } catch { /* optional storage */ } }
function pendingActionKey(playerId: string) { return `fortuneforge:video-poker:pending:${playerId}` }
function readPendingAction(playerId: string): StoredPendingAction | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingActionKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const action = value as Record<string, unknown>
    if (action.operation === 'deal' && typeof action.idempotencyKey === 'string' && typeof action.coinsWagered === 'number') return { operation: 'deal', idempotencyKey: action.idempotencyKey, coinsWagered: action.coinsWagered, handCount: action.handCount === 3 || action.handCount === 5 ? action.handCount : 1 }
    if (action.operation === 'draw' && typeof action.idempotencyKey === 'string' && typeof action.roundId === 'string' && validPositions(action.heldPositions)) return { operation: 'draw', idempotencyKey: action.idempotencyKey, roundId: action.roundId, heldPositions: action.heldPositions }
  } catch { /* optional storage */ }
  return null
}
function storePendingAction(playerId: string | undefined, action: StoredPendingAction) { if (!playerId) return; try { sessionStorage.setItem(pendingActionKey(playerId), JSON.stringify(action)) } catch { /* optional storage */ } }
function clearPendingAction(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingActionKey(playerId)) } catch { /* optional storage */ } }
function holdsKey(playerId: string) { return `fortuneforge:video-poker:holds:${playerId}` }
function validPositions(value: unknown): value is VideoPokerCardPosition[] { return Array.isArray(value) && value.every(position => Number.isInteger(position) && position >= 0 && position <= 4) && new Set(value).size === value.length }
function readHolds(playerId: string | undefined, roundId: string): readonly VideoPokerCardPosition[] | null { if (!playerId) return null; try { const value = JSON.parse(sessionStorage.getItem(holdsKey(playerId)) ?? 'null'); return value?.roundId === roundId && validPositions(value.positions) ? value.positions : null } catch { return null } }
function storeHolds(playerId: string | undefined, roundId: string, positions: readonly VideoPokerCardPosition[]) { if (!playerId) return; try { sessionStorage.setItem(holdsKey(playerId), JSON.stringify({ roundId, positions })) } catch { /* optional storage */ } }
function clearHolds(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(holdsKey(playerId)) } catch { /* optional storage */ } }

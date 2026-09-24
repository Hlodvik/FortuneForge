import { useEffect, useMemo, useRef, useState } from 'react'
import {
  VideoPokerGatewayError,
  type VideoPokerCard,
  type VideoPokerCardPosition,
  type VideoPokerGateway,
  type VideoPokerHandCount,
  type VideoPokerRound,
  type VideoPokerStatus,
} from './contracts'
import { HttpVideoPokerGateway } from './httpVideoPokerGateway'
import './videoPoker.css'
import './videoPokerPaytable.css'
import './videoPokerEnhancements.css'
import './videoPokerViewport.css'

export type VideoPokerGameProps = Readonly<{
  gateway?: VideoPokerGateway
  playerId?: string
  currencySymbol?: string
  onBalanceChange?: (balance: number) => void
}>

const defaultGateway = new HttpVideoPokerGateway()
type StoredPendingAction =
  | Readonly<{ operation: 'deal'; idempotencyKey: string; coinsWagered: number; handCount: VideoPokerHandCount }>
  | Readonly<{ operation: 'draw'; idempotencyKey: string; roundId: string; heldPositions: readonly VideoPokerCardPosition[] }>

export function VideoPokerGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange }: VideoPokerGameProps) {
  const [status, setStatus] = useState<VideoPokerStatus | null>(null)
  const [round, setRound] = useState<VideoPokerRound | null>(null)
  const [coinsWagered, setCoinsWagered] = useState(1)
  const [handCount, setHandCount] = useState<VideoPokerHandCount>(1)
  const [heldPositions, setHeldPositions] = useState<readonly VideoPokerCardPosition[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [strategyHelp, setStrategyHelp] = useState(false)
  const dealRequestKey = useRef<string | null>(null)
  const drawRequestKey = useRef<string | null>(null)
  const pendingDealCoins = useRef<number | null>(null)
  const pendingDealHandCount = useRef<VideoPokerHandCount | null>(null)
  const pendingDrawPositions = useRef<readonly VideoPokerCardPosition[] | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setCoinsWagered(nextStatus.minimumCoinsWagered)
      setTableUnavailable(!nextStatus.available)
      setError(nextStatus.available ? null : 'This table is temporarily unavailable. Please choose another game.')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      const unavailable = isTableUnavailable(reason)
      setTableUnavailable(unavailable)
      setError(unavailable ? 'This table is temporarily unavailable. Please choose another game.' : messageForError(reason))
    })
    return () => controller.abort()
  }, [gateway, statusLoadAttempt])

  useEffect(() => {
    const pendingAction = playerId ? readPendingAction(playerId) : null
    if (pendingAction) {
      const controller = new AbortController()
      setRecovery('recovering')
      const request = pendingAction.operation === 'deal'
        ? gateway.createRound(pendingAction.coinsWagered, { signal: controller.signal, idempotencyKey: pendingAction.idempotencyKey, handCount: pendingAction.handCount })
        : gateway.draw(pendingAction.roundId, pendingAction.heldPositions, { signal: controller.signal, idempotencyKey: pendingAction.idempotencyKey })
      void request.then(nextRound => {
        clearPendingAction(playerId)
        if (nextRound.phase === 'awaiting-draw') storeRoundId(playerId, nextRound.roundId)
        else clearStoredRoundId(playerId)
        setRound(nextRound)
        setHandCount(nextRound.handCount)
        setHeldPositions(nextRound.heldPositions)
        onBalanceChange?.(nextRound.balance)
        setRecovery('ready')
      }).catch(reason => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return
        setRecovery('failed')
        setError('Your pending Video Poker request could not be restored. Retry restoration before starting another deal.')
      })
      return () => controller.abort()
    }
    const storedRoundId = playerId ? readStoredRoundId(playerId) : null
    if (!storedRoundId) {
      setRecovery('ready')
      return undefined
    }
    const controller = new AbortController()
    setRecovery('recovering')
    void gateway.getRound(storedRoundId, controller.signal).then(nextRound => {
      if (nextRound.phase === 'awaiting-draw') {
        setRound(nextRound)
        setHandCount(nextRound.handCount)
        setHeldPositions(nextRound.heldPositions)
      } else {
        clearStoredRoundId(playerId)
      }
      setRecovery('ready')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      if (reason instanceof VideoPokerGatewayError && reason.status === 404) {
        clearStoredRoundId(playerId)
        setRecovery('ready')
        return
      }
      setRecovery('failed')
      setError('Your unfinished hand could not be restored. Retry restoration before starting another deal.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  const wagerOptions = useMemo(() => status
    ? Array.from(
      { length: status.maximumCoinsWagered - status.minimumCoinsWagered + 1 },
      (_, index) => status.minimumCoinsWagered + index,
    )
    : [], [status])
  const balance = round?.balance ?? status?.balance ?? null
  const awaitingDraw = round?.phase === 'awaiting-draw'
  const completed = round?.phase === 'completed'
  const paytable = paytableRows(round?.coinsWagered ?? coinsWagered)
  const suggestedHolds = awaitingDraw && round?.initialCards ? recommendedHolds(round.initialCards) : []

  const deal = (coinOverride?: number, handCountOverride?: VideoPokerHandCount) => {
    if (!status?.available || busy || recovery !== 'ready') return
    const idempotencyKey = dealRequestKey.current ??= createRequestKey('video-poker-deal')
    const coins = pendingDealCoins.current ?? coinOverride ?? coinsWagered
    const hands = pendingDealHandCount.current ?? handCountOverride ?? handCount
    pendingDealCoins.current = coins
    pendingDealHandCount.current = hands
    storePendingAction(playerId, { operation: 'deal', idempotencyKey, coinsWagered: coins, handCount: hands })
    void act(async () => {
      const nextRound = await gateway.createRound(coins, { idempotencyKey, handCount: hands })
      dealRequestKey.current = null
      pendingDealCoins.current = null
      pendingDealHandCount.current = null
      clearPendingAction(playerId)
      if (nextRound.phase === 'awaiting-draw') storeRoundId(playerId, nextRound.roundId)
      setRound(nextRound)
      setHandCount(nextRound.handCount)
      setHeldPositions(nextRound.heldPositions)
      onBalanceChange?.(nextRound.balance)
    })
  }

  const toggleHold = (position: VideoPokerCardPosition) => {
    if (!awaitingDraw || busy) return
    setHeldPositions(current => current.includes(position)
      ? current.filter(value => value !== position)
      : [...current, position].sort() as VideoPokerCardPosition[])
  }

  const draw = () => {
    if (!round || !awaitingDraw || busy) return
    const idempotencyKey = drawRequestKey.current ??= createRequestKey('video-poker-draw')
    const positions = pendingDrawPositions.current ?? heldPositions
    pendingDrawPositions.current = positions
    storePendingAction(playerId, { operation: 'draw', idempotencyKey, roundId: round.roundId, heldPositions: positions })
    void act(async () => {
      const nextRound = await gateway.draw(round.roundId, positions, { idempotencyKey })
      drawRequestKey.current = null
      pendingDrawPositions.current = null
      clearPendingAction(playerId)
      clearStoredRoundId(playerId)
      setRound(nextRound)
      setHeldPositions(nextRound.heldPositions)
      onBalanceChange?.(nextRound.balance)
    })
  }

  const act = async (operation: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try {
      await operation()
    } catch (reason) {
      setError(messageForError(reason))
    } finally {
      setBusy(false)
    }
  }

  const newHand = () => {
    if (busy) return
    setRound(null)
    setHeldPositions([])
    setError(null)
    dealRequestKey.current = null
    drawRequestKey.current = null
    pendingDealCoins.current = null
    pendingDealHandCount.current = null
    pendingDrawPositions.current = null
    clearPendingAction(playerId)
    clearStoredRoundId(playerId)
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  return (
    <main className="ff-video-poker" aria-busy={busy}>
      <header className="ff-video-poker__header">
        <div><span className="ff-video-poker__eyebrow">Full-pay 9/6 · Jacks or Better</span><h1>Video Poker</h1></div>
        <div className="ff-video-poker__balance"><span>Balance</span><strong>{balance === null ? '—' : formatMoney(balance, currencySymbol)}</strong></div>
      </header>

      <section className="ff-video-poker__console" aria-label="Video Poker game">
        {!round && <section className="ff-video-poker__welcome">
          <div className="ff-video-poker__intro"><span aria-hidden="true">♠</span><h2>Make your hand</h2><p>Choose your wager, deal five cards, then hold the cards you want to keep.</p></div>
          <details className="ff-video-poker__paytable">
            <summary>Paytable <span>{coinsWagered} {coinsWagered === 1 ? 'coin' : 'coins'} wagered</span></summary>
            <table aria-label="Jacks or Better paytable">
              <thead><tr><th scope="col">Hand</th><th scope="col">Pays</th></tr></thead>
              <tbody>{paytable.map(row => <tr key={row.hand}><th scope="row">{row.hand}</th><td>{row.payout.toLocaleString()} {row.payout === 1 ? 'coin' : 'coins'}</td></tr>)}</tbody>
            </table>
            <p>Royal Flush pays 4,000 coins when five coins are wagered.</p>
          </details>
          <label className="ff-video-poker__wager">Coin wager
            <select value={coinsWagered} disabled={busy || recovery !== 'ready' || !status?.available || pendingDealCoins.current !== null} onChange={event => setCoinsWagered(Number(event.target.value))}>
              {wagerOptions.map(value => <option key={value} value={value}>{value} {value === 1 ? 'coin' : 'coins'} · {formatMoney(value * (status?.coinValue ?? 0), currencySymbol)}</option>)}
            </select>
          </label>
          <fieldset className="ff-video-poker__hand-count" disabled={busy || recovery !== 'ready' || !status?.available || pendingDealHandCount.current !== null}>
            <legend>Hands</legend>
            {([1, 3, 5] as const).map(value => <button key={value} type="button" aria-pressed={handCount === value} onClick={() => setHandCount(value)}>{value}</button>)}
            <small>Each hand uses the same deal and holds, then draws from its own shuffled deck.</small>
          </fieldset>
          <p className="ff-video-poker__total-wager">Total wager: <strong>{formatMoney(coinsWagered * handCount * (status?.coinValue ?? 0), currencySymbol)}</strong> · {coinsWagered} {coinsWagered === 1 ? 'coin' : 'coins'} × {handCount} {handCount === 1 ? 'hand' : 'hands'}</p>
          <div className="ff-video-poker__bet-controls"><button type="button" disabled={busy || !status} onClick={() => status && setCoinsWagered(value => value >= status.maximumCoinsWagered ? status.minimumCoinsWagered : value + 1)}>Bet 1</button><button type="button" disabled={busy || !status} onClick={() => { if (!status) return; setCoinsWagered(status.maximumCoinsWagered); deal(status.maximumCoinsWagered, handCount) }}>Bet Max &amp; Deal</button><button type="button" aria-pressed={strategyHelp} onClick={() => setStrategyHelp(value => !value)}>Strategy {strategyHelp ? 'On' : 'Off'}</button></div>
          <button className="ff-video-poker__primary" disabled={busy || recovery !== 'ready' || !status?.available} onClick={() => deal()}>
            {busy ? 'Dealing…' : pendingDealCoins.current ? 'Retry Deal' : 'Deal'}
          </button>
          {recovery === 'recovering' && <p className="ff-video-poker__loading" role="status">Restoring your unfinished hand…</p>}
          {recovery === 'failed' && <button className="ff-video-poker__primary" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
          {!status && !error && <p className="ff-video-poker__loading">Connecting to the table…</p>}
          {!status && error && !tableUnavailable && <button className="ff-video-poker__primary" type="button" onClick={retryStatus}>Retry connection</button>}
        </section>}

        {round && <section className="ff-video-poker__round" aria-live="polite">
          <div className="ff-video-poker__round-meta"><span>{round.coinsWagered} {round.coinsWagered === 1 ? 'coin' : 'coins'} × {round.handCount} {round.handCount === 1 ? 'hand' : 'hands'} · {formatMoney(round.wager, currencySymbol)} wagered</span><strong>{awaitingDraw ? 'Choose cards to hold' : `${round.handCount === 1 ? 'Hand' : 'Hands'} complete`}</strong></div>
          {awaitingDraw && <div className="ff-video-poker__cards" aria-label="Your shared dealt cards">
            {round.initialCards.map((card, index) => <CardButton key={index} card={card} position={index as VideoPokerCardPosition} held={heldPositions.includes(index as VideoPokerCardPosition)} disabled={busy || pendingDrawPositions.current !== null} onToggle={toggleHold} />)}
          </div>}
          {completed && <div className="ff-video-poker__completed-hands" aria-label="Your final hands">
            {round.finalHands?.map((cards, handIndex) => <section className="ff-video-poker__completed-hand" key={handIndex}>
              <header><span>Hand {handIndex + 1}</span><strong>{humanizeHandRank(round.handRanks?.[handIndex] ?? null)}</strong><em>{formatMoney(round.handPayouts?.[handIndex] ?? 0, currencySymbol)}</em></header>
              <div className="ff-video-poker__cards">{cards.map((card, cardIndex) => <CardFace key={cardIndex} card={card} />)}</div>
            </section>)}
          </div>}
          <details className="ff-video-poker__paytable ff-video-poker__paytable--round" open={completed}><summary>Paytable · {round.coinsWagered} coins</summary><table><tbody>{paytable.map(row => <tr className={completed && humanizeHandRank(round.handRank) === row.hand ? 'is-winning-row' : ''} key={row.hand}><th>{row.hand}</th><td>{row.payout}</td></tr>)}</tbody></table></details>
          {awaitingDraw && <div className="ff-video-poker__actions">
            <p>{heldPositions.length === 0 ? 'No cards held — all five will be replaced.' : `${heldPositions.length} ${heldPositions.length === 1 ? 'card' : 'cards'} held.`}</p>
            {strategyHelp && <p className="ff-video-poker__strategy" role="status">{suggestedHolds.length === 0 ? 'No made pair or high-card hold found; drawing five is reasonable.' : `Strategy check: consider holding ${suggestedHolds.map(position => cardName(round.initialCards[position])).join(', ')}.${samePositions(suggestedHolds, heldPositions) ? ' Your holds match this simple guide.' : ' Review your holds before drawing.'}`}</p>}
            <button className="ff-video-poker__primary" disabled={busy} onClick={draw}>{busy ? 'Drawing…' : pendingDrawPositions.current ? 'Retry Draw' : 'Draw'}</button>
          </div>}
          {completed && <div className="ff-video-poker__result">
            <span>{round.handCount === 1 ? 'Final hand' : `${round.handCount} final hands`}</span><strong>{round.handCount === 1 ? `Result: ${humanizeHandRank(round.handRank)}` : 'Combined result'}</strong><p>{round.payout === 0 ? 'No winning hand this round.' : `${formatMoney(round.payout ?? 0, currencySymbol)} total won`}</p>
            <button className="ff-video-poker__primary" disabled={busy} onClick={newHand}>New Hand</button>
          </div>}
        </section>}
      </section>
      {error && <p className="ff-video-poker__error" role="alert">{error}</p>}
    </main>
  )
}

function CardButton({ card, position, held, disabled, onToggle }: Readonly<{ card: VideoPokerCard; position: VideoPokerCardPosition; held: boolean; disabled: boolean; onToggle: (position: VideoPokerCardPosition) => void }>) {
  const label = `${held ? 'Release' : 'Hold'} ${cardName(card)}`
  return <button className={`ff-video-poker__card-button${held ? ' is-held' : ''}`} type="button" aria-pressed={held} aria-label={label} disabled={disabled} onClick={() => onToggle(position)}>
    <CardFace card={card} />
    <span className="ff-video-poker__hold-label">{held ? 'HELD' : 'HOLD'}</span>
  </button>
}

function CardFace({ card }: Readonly<{ card: VideoPokerCard }>) {
  const symbol = suitSymbol(card.suit)
  const red = card.suit === 'diamonds' || card.suit === 'hearts'
  return <div className={`ff-video-poker__card${red ? ' is-red' : ''}`} aria-label={cardName(card)}>
    <span className="ff-video-poker__corner">{shortRank(card.rank)}<small>{symbol}</small></span><span className="ff-video-poker__suit" aria-hidden="true">{symbol}</span><span className="ff-video-poker__corner ff-video-poker__corner--reverse">{shortRank(card.rank)}<small>{symbol}</small></span>
  </div>
}

function shortRank(rank: VideoPokerCard['rank']) { return ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[rank] }
function suitSymbol(suit: VideoPokerCard['suit']) { return ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[suit] }
function cardName(card: VideoPokerCard) { return `${card.rank} of ${card.suit}` }
function humanizeHandRank(rank: VideoPokerRound['handRank']) { return rank ? rank.split('-').map(word => word[0]?.toUpperCase() + word.slice(1)).join(' ') : 'Result unavailable' }
function formatMoney(value: number, currencySymbol: string) { return `${currencySymbol}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function paytableRows(coinsWagered: number) { return [
  { hand: 'Royal Flush', payout: coinsWagered === 5 ? 4_000 : 250 * coinsWagered },
  { hand: 'Straight Flush', payout: 50 * coinsWagered },
  { hand: 'Four of a Kind', payout: 25 * coinsWagered },
  { hand: 'Full House', payout: 9 * coinsWagered },
  { hand: 'Flush', payout: 6 * coinsWagered },
  { hand: 'Straight', payout: 4 * coinsWagered },
  { hand: 'Three of a Kind', payout: 3 * coinsWagered },
  { hand: 'Two Pair', payout: 2 * coinsWagered },
  { hand: 'Jacks or Better', payout: coinsWagered },
] }
function recommendedHolds(cards: readonly VideoPokerCard[]): VideoPokerCardPosition[] {
  const groups = new Map<string, number[]>()
  cards.forEach((card, index) => groups.set(card.rank, [...(groups.get(card.rank) ?? []), index]))
  const made = [...groups.values()].filter(indices => indices.length >= 2).flat()
  if (made.length > 0) return made.sort() as VideoPokerCardPosition[]
  return cards.map((card, index) => ({ card, index })).filter(({ card }) => ['jack', 'queen', 'king', 'ace'].includes(card.rank)).map(({ index }) => index as VideoPokerCardPosition)
}
function samePositions(left: readonly number[], right: readonly number[]): boolean { return left.length === right.length && left.every((value, index) => value === [...right].sort()[index]) }
function messageForError(reason: unknown) { return reason instanceof VideoPokerGatewayError ? reason.message : 'The Video Poker service is unavailable.' }
function isTableUnavailable(reason: unknown) { return reason instanceof VideoPokerGatewayError && reason.code === 'video-poker-disabled' }
function createRequestKey(prefix: string) { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `${prefix}-${random}` }
function storedRoundKey(playerId: string) { return `fortuneforge:video-poker:round:${playerId}` }
function readStoredRoundId(playerId: string) { try { return sessionStorage.getItem(storedRoundKey(playerId)) } catch { return null } }
function storeRoundId(playerId: string | undefined, roundId: string) { if (!playerId) return; try { sessionStorage.setItem(storedRoundKey(playerId), roundId) } catch { /* session storage is optional */ } }
function clearStoredRoundId(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(storedRoundKey(playerId)) } catch { /* session storage is optional */ } }
function pendingActionKey(playerId: string) { return `fortuneforge:video-poker:pending:${playerId}` }
function readPendingAction(playerId: string): StoredPendingAction | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingActionKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const action = value as Record<string, unknown>
    if (action.operation === 'deal' && typeof action.idempotencyKey === 'string' && typeof action.coinsWagered === 'number') return { operation: 'deal', idempotencyKey: action.idempotencyKey, coinsWagered: action.coinsWagered, handCount: action.handCount === 3 || action.handCount === 5 ? action.handCount : 1 }
    if (action.operation === 'draw' && typeof action.idempotencyKey === 'string' && typeof action.roundId === 'string' && Array.isArray(action.heldPositions) && action.heldPositions.every(position => Number.isInteger(position) && position >= 0 && position <= 4)) return { operation: 'draw', idempotencyKey: action.idempotencyKey, roundId: action.roundId, heldPositions: action.heldPositions as VideoPokerCardPosition[] }
  } catch { /* session storage is optional */ }
  return null
}
function storePendingAction(playerId: string | undefined, action: StoredPendingAction) { if (!playerId) return; try { sessionStorage.setItem(pendingActionKey(playerId), JSON.stringify(action)) } catch { /* session storage is optional */ } }
function clearPendingAction(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingActionKey(playerId)) } catch { /* session storage is optional */ } }

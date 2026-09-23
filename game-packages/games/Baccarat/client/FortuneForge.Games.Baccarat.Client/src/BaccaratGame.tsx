import { useEffect, useRef, useState } from 'react'
import {
  BaccaratGatewayError,
  type BaccaratBetSide,
  type BaccaratCard,
  type BaccaratGateway,
  type BaccaratRound,
  type BaccaratStatus,
} from './contracts'
import { HttpBaccaratGateway } from './httpBaccaratGateway'
import './baccarat.css'

export type BaccaratGameProps = Readonly<{
  gateway?: BaccaratGateway
  playerId?: string
  currencySymbol?: string
  onBalanceChange?: (balance: number) => void
}>

const defaultGateway = new HttpBaccaratGateway()

type StoredPendingDeal = Readonly<{ idempotencyKey: string; betSide: BaccaratBetSide; stake: number }>

export function BaccaratGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange }: BaccaratGameProps) {
  const [status, setStatus] = useState<BaccaratStatus | null>(null)
  const [round, setRound] = useState<BaccaratRound | null>(null)
  const [betSide, setBetSide] = useState<BaccaratBetSide>('player')
  const [stake, setStake] = useState(1)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const dealRequestKey = useRef<string | null>(null)
  const pendingBet = useRef<Readonly<{ side: BaccaratBetSide; stake: number }> | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setStake(nextStatus.minimumStake)
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
    const pendingDeal = playerId ? readPendingDeal(playerId) : null
    if (!pendingDeal) {
      setRecovery('ready')
      return undefined
    }
    const controller = new AbortController()
    setRecovery('recovering')
    void gateway.createRound(pendingDeal.betSide, pendingDeal.stake, {
      signal: controller.signal,
      idempotencyKey: pendingDeal.idempotencyKey,
    }).then(nextRound => {
      clearPendingDeal(playerId)
      setRound(nextRound)
      onBalanceChange?.(nextRound.balance)
      setRecovery('ready')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      setRecovery('failed')
      setError('Your pending Baccarat deal could not be restored. Retry restoration before starting another hand.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  const balance = round?.balance ?? status?.balance ?? null
  const stakeIsValid = status !== null && stake >= status.minimumStake && stake <= status.maximumStake &&
    isIncrementAligned(stake, status.minimumStake, status.stakeIncrement)

  const act = async (operation: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try { await operation() } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }

  const submitDeal = (bet: Readonly<{ side: BaccaratBetSide; stake: number }>, idempotencyKey: string) => {
    pendingBet.current = bet
    storePendingDeal(playerId, { idempotencyKey, betSide: bet.side, stake: bet.stake })
    void act(async () => {
      const nextRound = await gateway.createRound(bet.side, bet.stake, { idempotencyKey })
      dealRequestKey.current = null
      pendingBet.current = null
      clearPendingDeal(playerId)
      setRound(nextRound)
      onBalanceChange?.(nextRound.balance)
    })
  }

  const deal = () => {
    if (!status?.available || busy || recovery !== 'ready' || (!pendingBet.current && !stakeIsValid)) return
    const idempotencyKey = dealRequestKey.current ??= createRequestKey()
    const bet = pendingBet.current ?? { side: betSide, stake }
    submitDeal(bet, idempotencyKey)
  }

  const dealAgain = () => {
    if (busy || recovery !== 'ready' || !status?.available || round === null) return
    const bet = { side: round.betSide, stake: round.stake }
    setRound(null)
    setBetSide(bet.side)
    setStake(bet.stake)
    setError(null)
    const idempotencyKey = createRequestKey()
    dealRequestKey.current = idempotencyKey
    submitDeal(bet, idempotencyKey)
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  return (
    <main className="ff-baccarat" aria-busy={busy}>
      <header className="ff-baccarat__header">
        <div><span className="ff-baccarat__eyebrow">Punto Banco · Standard commission</span><h1>Baccarat</h1></div>
        <div className="ff-baccarat__account"><span>Balance</span><strong>{balance === null ? '—' : formatMoney(balance, currencySymbol)}</strong><small>{status?.available ? 'Table open' : status ? 'Table unavailable' : 'Connecting…'}</small></div>
      </header>

      <section className="ff-baccarat__table" aria-label="Baccarat table">
        {!round && <section className="ff-baccarat__betting">
          <div className="ff-baccarat__table-mark"><span aria-hidden="true">♣</span><h2>Place your bet</h2><p>One hand settles automatically under the Punto Banco tableau.</p></div>
          <div className="ff-baccarat__bet-sides" role="group" aria-label="Baccarat bet side">
            <BetButton side="player" current={betSide} label="Player" payout="1:1" disabled={busy || recovery !== 'ready' || !status?.available || pendingBet.current !== null} onSelect={setBetSide} />
            <BetButton side="banker" current={betSide} label="Banker" payout="0.95:1 after commission" disabled={busy || recovery !== 'ready' || !status?.available || pendingBet.current !== null} onSelect={setBetSide} />
            <BetButton side="tie" current={betSide} label="Tie" payout="8:1" disabled={busy || recovery !== 'ready' || !status?.available || pendingBet.current !== null} onSelect={setBetSide} />
          </div>
          <label className="ff-baccarat__stake">Stake
            <input aria-label="Stake" type="number" value={stake} min={status?.minimumStake} max={status?.maximumStake} step={status?.stakeIncrement} disabled={busy || recovery !== 'ready' || !status?.available || pendingBet.current !== null} onChange={event => setStake(Number(event.target.value))} />
            {status && <small>Min {formatMoney(status.minimumStake, currencySymbol)} · Max {formatMoney(status.maximumStake, currencySymbol)} · Step {formatMoney(status.stakeIncrement, currencySymbol)}</small>}
          </label>
          <button className="ff-baccarat__primary" disabled={busy || recovery !== 'ready' || !status?.available || (!pendingBet.current && !stakeIsValid)} onClick={deal}>{busy ? 'Dealing…' : pendingBet.current ? 'Retry Deal' : 'Deal'}</button>
          {recovery === 'recovering' && <p className="ff-baccarat__loading" role="status">Restoring your pending Baccarat deal…</p>}
          {recovery === 'failed' && <button className="ff-baccarat__primary" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
          {!status && error && !tableUnavailable && <button className="ff-baccarat__primary" type="button" onClick={retryStatus}>Retry connection</button>}
        </section>}

        {round && <section className="ff-baccarat__settled" aria-live="polite">
          <div className="ff-baccarat__round-head"><span>{round.endedOnNatural ? 'Natural' : 'Tableau complete'}</span><strong>{outcomeLabel(round.outcome)}</strong></div>
          <Hand label="Player" cards={round.playerCards} total={round.playerTotal} winner={round.outcome === 'player'} />
          <Hand label="Banker" cards={round.bankerCards} total={round.bankerTotal} winner={round.outcome === 'banker'} />
          <div className="ff-baccarat__settlement">
            <div><span>Bet</span><strong>{capitalize(round.betSide)} · {formatMoney(round.stake, currencySymbol)}</strong></div>
            <div><span>Result</span><strong className={`is-${round.disposition}`}>{capitalize(round.disposition)}</strong></div>
            <div><span>Profit</span><strong>{formatSignedMoney(round.profit, currencySymbol)}</strong></div>
            <div><span>Total return</span><strong>{formatMoney(round.totalReturn, currencySymbol)}</strong></div>
          </div>
          <button className="ff-baccarat__primary" disabled={busy} onClick={dealAgain}>Deal Again</button>
        </section>}
      </section>
      {error && <p className="ff-baccarat__error" role="alert">{error}</p>}
    </main>
  )
}

function BetButton({ side, current, label, payout, disabled, onSelect }: Readonly<{ side: BaccaratBetSide; current: BaccaratBetSide; label: string; payout: string; disabled: boolean; onSelect: (side: BaccaratBetSide) => void }>) {
  const selected = side === current
  return <button type="button" className={`ff-baccarat__bet${selected ? ' is-selected' : ''}`} aria-pressed={selected} disabled={disabled} onClick={() => onSelect(side)}><strong>{label}</strong><span>{payout}</span></button>
}

function Hand({ label, cards, total, winner }: Readonly<{ label: string; cards: readonly BaccaratCard[]; total: number; winner: boolean }>) {
  return <section className={`ff-baccarat__hand${winner ? ' is-winner' : ''}`} aria-label={`${label} hand, total ${total}`}><div className="ff-baccarat__hand-title"><span>{label}</span><strong>Total {total}</strong></div><div className="ff-baccarat__cards">{cards.map((card, index) => <CardFace key={index} card={card} />)}</div></section>
}

function CardFace({ card }: Readonly<{ card: BaccaratCard }>) {
  const symbol = suitSymbol(card.suit)
  const red = card.suit === 'diamonds' || card.suit === 'hearts'
  return <div className={`ff-baccarat__card${red ? ' is-red' : ''}`} aria-label={`${card.rank} of ${card.suit}`}><span className="ff-baccarat__corner">{shortRank(card.rank)}<small>{symbol}</small></span><span className="ff-baccarat__suit" aria-hidden="true">{symbol}</span><span className="ff-baccarat__corner ff-baccarat__corner--reverse">{shortRank(card.rank)}<small>{symbol}</small></span></div>
}

function isIncrementAligned(value: number, minimum: number, increment: number) {
  const steps = (value - minimum) / increment
  return Number.isFinite(steps) && Math.abs(steps - Math.round(steps)) < 1e-9
}
function shortRank(rank: BaccaratCard['rank']) { return ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[rank] }
function suitSymbol(suit: BaccaratCard['suit']) { return ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[suit] }
function outcomeLabel(outcome: BaccaratRound['outcome']) { return `${capitalize(outcome)} wins` }
function capitalize(value: string) { return value[0]?.toUpperCase() + value.slice(1) }
function formatMoney(value: number, currencySymbol: string) { return `${value < 0 ? '-' : ''}${currencySymbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function formatSignedMoney(value: number, currencySymbol: string) { return `${value > 0 ? '+' : ''}${formatMoney(value, currencySymbol)}` }
function messageForError(reason: unknown) { return reason instanceof BaccaratGatewayError ? reason.message : 'The Baccarat table is unavailable.' }
function isTableUnavailable(reason: unknown) { return reason instanceof BaccaratGatewayError && reason.code === 'baccarat-disabled' }
function createRequestKey() { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `baccarat-${random}` }
function pendingDealKey(playerId: string) { return `fortuneforge:baccarat:pending:${playerId}` }
function readPendingDeal(playerId: string): StoredPendingDeal | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingDealKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const deal = value as Record<string, unknown>
    if (typeof deal.idempotencyKey === 'string' && typeof deal.stake === 'number' && (deal.betSide === 'player' || deal.betSide === 'banker' || deal.betSide === 'tie')) {
      return { idempotencyKey: deal.idempotencyKey, betSide: deal.betSide, stake: deal.stake }
    }
  } catch { /* session storage is optional */ }
  return null
}
function storePendingDeal(playerId: string | undefined, deal: StoredPendingDeal) { if (!playerId) return; try { sessionStorage.setItem(pendingDealKey(playerId), JSON.stringify(deal)) } catch { /* session storage is optional */ } }
function clearPendingDeal(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingDealKey(playerId)) } catch { /* session storage is optional */ } }

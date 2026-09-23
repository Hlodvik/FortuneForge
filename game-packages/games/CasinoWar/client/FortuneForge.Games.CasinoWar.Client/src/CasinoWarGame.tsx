import { useEffect, useRef, useState } from 'react'
import {
  CasinoWarGatewayError,
  type CasinoWarCard,
  type CasinoWarDecision,
  type CasinoWarGateway,
  type CasinoWarRound,
  type CasinoWarStatus,
} from './contracts'
import { HttpCasinoWarGateway } from './httpCasinoWarGateway'
import './casinoWar.css'

export type CasinoWarGameProps = Readonly<{
  gateway?: CasinoWarGateway
  playerId?: string
  currencySymbol?: string
  onBalanceChange?: (balance: number) => void
}>

const defaultGateway = new HttpCasinoWarGateway()

type StoredPendingAction =
  | Readonly<{ operation: 'opening'; idempotencyKey: string; primaryStake: number; tieStake: number }>
  | Readonly<{ operation: 'decision'; idempotencyKey: string; roundId: string; decision: CasinoWarDecision }>

export function CasinoWarGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange }: CasinoWarGameProps) {
  const [status, setStatus] = useState<CasinoWarStatus | null>(null)
  const [round, setRound] = useState<CasinoWarRound | null>(null)
  const [primaryStake, setPrimaryStake] = useState(1)
  const [tieStake, setTieStake] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [revealStage, setRevealStage] = useState(0)
  const openingRequestKey = useRef<string | null>(null)
  const decisionRequestKey = useRef<string | null>(null)
  const pendingOpeningStakes = useRef<Readonly<{ primaryStake: number; tieStake: number }> | null>(null)
  const pendingDecision = useRef<CasinoWarDecision | null>(null)

  useEffect(() => {
    if (!round) { setRevealStage(0); return }
    const hasWarCards = round.phase === 'completed' && (round.playerWarCard || round.dealerWarCard)
    setRevealStage(hasWarCards ? 2 : 0)
    const targets = hasWarCards ? [3, 4] : [1, 2]
    const timers = targets.map((target, index) => window.setTimeout(() => setRevealStage(target), 180 + index * 280))
    return () => timers.forEach(timer => window.clearTimeout(timer))
  }, [round?.phase, round?.roundId])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setPrimaryStake(nextStatus.minimumPrimaryStake)
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
      const request = pendingAction.operation === 'opening'
        ? gateway.createRound(pendingAction.primaryStake, pendingAction.tieStake, { signal: controller.signal, idempotencyKey: pendingAction.idempotencyKey })
        : gateway.decide(pendingAction.roundId, pendingAction.decision, { signal: controller.signal, idempotencyKey: pendingAction.idempotencyKey })
      void request.then(nextRound => {
        clearPendingAction(playerId)
        if (nextRound.phase === 'awaiting-tie-decision') storeRoundId(playerId, nextRound.roundId)
        else clearStoredRoundId(playerId)
        setRound(nextRound)
        onBalanceChange?.(nextRound.balance)
        setRecovery('ready')
      }).catch(reason => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return
        setRecovery('failed')
        setError('Your pending Casino War request could not be restored. Retry restoration before starting another round.')
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
      if (nextRound.phase === 'awaiting-tie-decision') setRound(nextRound)
      else clearStoredRoundId(playerId)
      setRecovery('ready')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      if (reason instanceof CasinoWarGatewayError && reason.status === 404) {
        clearStoredRoundId(playerId)
        setRecovery('ready')
        return
      }
      setRecovery('failed')
      setError('Your pending Casino War request could not be restored. Retry restoration before starting another round.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  const balance = round?.balance ?? status?.balance ?? null
  const primaryIsValid = status !== null && primaryStake >= status.minimumPrimaryStake &&
    primaryStake <= status.maximumPrimaryStake && isIncrementAligned(primaryStake, status.minimumPrimaryStake, status.stakeIncrement)
  const tieIsValid = status !== null && tieStake >= 0 && tieStake <= status.maximumTieStake && Number.isFinite(tieStake) &&
    (tieStake === 0 || isIncrementAligned(tieStake, 0, status.stakeIncrement))

  const act = async (operation: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try { await operation() } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }

  const deal = () => {
    if (!status?.available || busy || recovery !== 'ready' || (!pendingOpeningStakes.current && (!primaryIsValid || !tieIsValid))) return
    const idempotencyKey = openingRequestKey.current ??= createRequestKey('opening')
    const stakes = pendingOpeningStakes.current ?? { primaryStake, tieStake }
    pendingOpeningStakes.current = stakes
    storePendingAction(playerId, { operation: 'opening', idempotencyKey, primaryStake: stakes.primaryStake, tieStake: stakes.tieStake })
    void act(async () => {
      const nextRound = await gateway.createRound(stakes.primaryStake, stakes.tieStake, { idempotencyKey })
      openingRequestKey.current = null
      pendingOpeningStakes.current = null
      clearPendingAction(playerId)
      if (nextRound.phase === 'awaiting-tie-decision') storeRoundId(playerId, nextRound.roundId)
      setRound(nextRound)
      onBalanceChange?.(nextRound.balance)
    })
  }

  const decide = (decision: CasinoWarDecision) => {
    if (!round || round.phase !== 'awaiting-tie-decision' || busy) return
    if (pendingDecision.current && pendingDecision.current !== decision) {
      setError(`Your ${humanize(pendingDecision.current)} decision is still pending. Retry that decision so the table can return the original result.`)
      return
    }
    const idempotencyKey = decisionRequestKey.current ??= createRequestKey('decision')
    pendingDecision.current = decision
    storePendingAction(playerId, { operation: 'decision', idempotencyKey, roundId: round.roundId, decision })
    void act(async () => {
      const nextRound = await gateway.decide(round.roundId, decision, { idempotencyKey })
      decisionRequestKey.current = null
      pendingDecision.current = null
      clearPendingAction(playerId)
      clearStoredRoundId(playerId)
      setRound(nextRound)
      onBalanceChange?.(nextRound.balance)
    })
  }

  const reset = () => {
    if (busy) return
    setRound(null)
    setError(null)
    openingRequestKey.current = null
    decisionRequestKey.current = null
    pendingOpeningStakes.current = null
    pendingDecision.current = null
    clearPendingAction(playerId)
    clearStoredRoundId(playerId)
  }

  const rebet = () => {
    if (!round || round.phase !== 'completed' || busy || recovery !== 'ready' || !status?.available) return
    const stakes = { primaryStake: round.primaryStake, tieStake: round.tieStake }
    setPrimaryStake(stakes.primaryStake)
    setTieStake(stakes.tieStake)
    setRound(null)
    const idempotencyKey = createRequestKey('opening')
    openingRequestKey.current = idempotencyKey
    pendingOpeningStakes.current = stakes
    storePendingAction(playerId, { operation: 'opening', idempotencyKey, ...stakes })
    void act(async () => {
      const nextRound = await gateway.createRound(stakes.primaryStake, stakes.tieStake, { idempotencyKey })
      openingRequestKey.current = null
      pendingOpeningStakes.current = null
      clearPendingAction(playerId)
      if (nextRound.phase === 'awaiting-tie-decision') storeRoundId(playerId, nextRound.roundId)
      setRound(nextRound)
      onBalanceChange?.(nextRound.balance)
    })
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  return (
    <main className="ff-casino-war" aria-busy={busy}>
      <header className="ff-casino-war__header">
        <div><span className="ff-casino-war__eyebrow">Ace high · Player-favored second tie</span><h1>Casino War</h1></div>
        <div className="ff-casino-war__account"><span>Balance</span><strong>{balance === null ? '—' : formatMoney(balance, currencySymbol)}</strong><small>{status?.available ? 'Table open' : tableUnavailable || status ? 'Table unavailable' : 'Connecting…'}</small></div>
      </header>

      <section className="ff-casino-war__table" aria-label="Casino War table">
        {!round && <section className="ff-casino-war__betting">
          <div className="ff-casino-war__intro"><span aria-hidden="true">⚔</span><h2>Choose your stakes</h2><p>Win the opening comparison 1:1. An opening tie lets you Surrender or Go to War.</p></div>
          <div className="ff-casino-war__stakes">
            <StakeField label="Primary stake" value={primaryStake} min={status?.minimumPrimaryStake} max={status?.maximumPrimaryStake} step={status?.stakeIncrement} disabled={busy || recovery !== 'ready' || !status?.available || pendingOpeningStakes.current !== null} onChange={setPrimaryStake} hint={status ? `Min ${formatMoney(status.minimumPrimaryStake, currencySymbol)} · Max ${formatMoney(status.maximumPrimaryStake, currencySymbol)} · Step ${formatMoney(status.stakeIncrement, currencySymbol)}` : undefined} />
            <StakeField label="Tie stake (optional)" value={tieStake} min={0} max={status?.maximumTieStake} step={status?.stakeIncrement} disabled={busy || recovery !== 'ready' || !status?.available || pendingOpeningStakes.current !== null} onChange={setTieStake} hint={status ? `0 to ${formatMoney(status.maximumTieStake, currencySymbol)} · Tie pays 10:1 profit` : undefined} />
          </div>
          <div className="ff-casino-war__rules"><span>Tie: surrender for half your primary stake, or add a matching stake to go to War.</span><span>War burns 3 cards before each card; a second tie favors Player.</span></div>
          <button className="ff-casino-war__primary" disabled={busy || recovery !== 'ready' || !status?.available || (!pendingOpeningStakes.current && (!primaryIsValid || !tieIsValid))} onClick={deal}>{busy ? 'Dealing…' : pendingOpeningStakes.current ? 'Retry Deal' : 'Deal'}</button>
          {recovery === 'recovering' && <p className="ff-casino-war__loading" role="status">Restoring your pending Casino War request…</p>}
          {recovery === 'failed' && <button className="ff-casino-war__secondary" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
          {!status && error && !tableUnavailable && <button className="ff-casino-war__secondary" type="button" onClick={retryStatus}>Retry connection</button>}
        </section>}

        {round && <section className="ff-casino-war__round" aria-live="polite">
          <div className="ff-casino-war__round-heading"><span>{round.phase === 'awaiting-tie-decision' ? 'Opening tie' : 'Round complete'}</span><strong>{round.phase === 'awaiting-tie-decision' ? 'Choose your move' : primaryOutcomeLabel(round)}</strong></div>
          <div className="ff-casino-war__cards-grid">
            <CardGroup label="Player opening card" card={revealStage >= 1 ? round.playerOpeningCard : null} />
            <CardGroup label="Dealer opening card" card={revealStage >= 2 ? round.dealerOpeningCard : null} />
          </div>
          {round.phase === 'awaiting-tie-decision' && revealStage >= 2 && <section className="ff-casino-war__decision" aria-label="Tie decision">
            <p>Opening tie. <strong>Go to War adds one matching {formatMoney(round.primaryStake, currencySymbol)} primary stake.</strong></p>
            <div className="ff-casino-war__decision-consequences"><span><b>Surrender</b>End now and recover half of the primary stake.</span><span><b>Go to War</b>Add {formatMoney(round.primaryStake, currencySymbol)}, burn three cards each, then compare again. A second tie favors you.</span></div>
            <div><button className="ff-casino-war__secondary" disabled={busy || (pendingDecision.current !== null && pendingDecision.current !== 'surrender')} onClick={() => decide('surrender')}>{pendingDecision.current === 'surrender' ? 'Retry Surrender' : 'Surrender'}</button><button className="ff-casino-war__primary" disabled={busy || (pendingDecision.current !== null && pendingDecision.current !== 'go-to-war')} onClick={() => decide('go-to-war')}>{busy ? 'Resolving…' : pendingDecision.current === 'go-to-war' ? 'Retry Go to War' : 'Go to War'}</button></div>
          </section>}
          {round.phase === 'awaiting-tie-decision' && round.tieSettlement && <TieSettlement settlement={round.tieSettlement} currencySymbol={currencySymbol} />}
          {round.phase === 'completed' && <>
            {(round.playerWarCard || round.dealerWarCard) && <div className="ff-casino-war__cards-grid ff-casino-war__cards-grid--war">
              <CardGroup label="Player War card" card={revealStage >= 3 ? round.playerWarCard : null} />
              <CardGroup label="Dealer War card" card={revealStage >= 4 ? round.dealerWarCard : null} />
            </div>}
            {revealStage >= ((round.playerWarCard || round.dealerWarCard) ? 4 : 2) && <>
              <Settlements round={round} currencySymbol={currencySymbol} />
              <div className="ff-casino-war__round-actions"><button className="ff-casino-war__secondary" disabled={busy} onClick={rebet}>Rebet</button><button className="ff-casino-war__primary" disabled={busy} onClick={reset}>New Round</button></div>
            </>}
          </>}
        </section>}
      </section>
      {error && <p className="ff-casino-war__error" role="alert">{error}</p>}
    </main>
  )
}

function StakeField({ label, value, min, max, step, disabled, onChange, hint }: Readonly<{ label: string; value: number; min?: number; max?: number; step?: number; disabled: boolean; onChange: (value: number) => void; hint?: string }>) {
  return <label className="ff-casino-war__stake">{label}<input aria-label={label} type="number" value={value} min={min} max={max} step={step} disabled={disabled} onChange={event => onChange(Number(event.target.value))} />{hint && <small>{hint}</small>}</label>
}

function CardGroup({ label, card }: Readonly<{ label: string; card: CasinoWarCard | null }>) {
  return <section className="ff-casino-war__card-group" aria-label={label}><span>{label}</span>{card ? <CardFace card={card} /> : <div className="ff-casino-war__card ff-casino-war__card--empty">—</div>}</section>
}

function CardFace({ card }: Readonly<{ card: CasinoWarCard }>) {
  const symbol = suitSymbol(card.suit)
  const red = card.suit === 'diamonds' || card.suit === 'hearts'
  return <div className={`ff-casino-war__card${red ? ' is-red' : ''}`} aria-label={`${card.rank} of ${card.suit}`}><span className="ff-casino-war__corner">{shortRank(card.rank)}<small>{symbol}</small></span><span className="ff-casino-war__suit" aria-hidden="true">{symbol}</span><span className="ff-casino-war__corner ff-casino-war__corner--reverse">{shortRank(card.rank)}<small>{symbol}</small></span></div>
}

function Settlements({ round, currencySymbol }: Readonly<{ round: CasinoWarRound; currencySymbol: string }>) {
  const net = (round.primarySettlement?.profit ?? 0) + (round.tieSettlement?.profit ?? 0)
  return <section className="ff-casino-war__settlements" aria-label="Round settlements">
    {round.primarySettlement && <Settlement title="Primary wager" disposition={round.primarySettlement.disposition} outcome={round.primarySettlement.outcome} wagered={round.primarySettlement.totalWagered} totalReturn={round.primarySettlement.totalReturn} profit={round.primarySettlement.profit} currencySymbol={currencySymbol} />}
    {round.tieSettlement && <Settlement title="Tie side bet" disposition={round.tieSettlement.disposition} outcome={round.tieSettlement.outcome} wagered={round.tieSettlement.stake} totalReturn={round.tieSettlement.totalReturn} profit={round.tieSettlement.profit} currencySymbol={currencySymbol} />}
    <div className="ff-casino-war__net"><span>Total net change</span><strong>{formatSignedMoney(net, currencySymbol)}</strong><small>Updated balance {formatMoney(round.balance, currencySymbol)}</small></div>
  </section>
}

function TieSettlement({ settlement, currencySymbol }: Readonly<{ settlement: NonNullable<CasinoWarRound['tieSettlement']>; currencySymbol: string }>) {
  return <section className="ff-casino-war__tie-result" aria-label="Tie side bet settlement"><span>Tie side bet</span><strong className={`is-${settlement.disposition}`}>{settlement.won ? 'Tie wins' : 'Tie loses'} · {formatSignedMoney(settlement.profit, currencySymbol)}</strong></section>
}

function Settlement({ title, disposition, outcome, wagered, totalReturn, profit, currencySymbol }: Readonly<{ title: string; disposition: string; outcome: string; wagered: number; totalReturn: number; profit: number; currencySymbol: string }>) {
  return <section className="ff-casino-war__settlement"><div><span>{title}</span><strong className={`is-${disposition}`}>{humanize(outcome)}</strong></div><div><span>Wagered</span><strong>{formatMoney(wagered, currencySymbol)}</strong></div><div><span>Return</span><strong>{formatMoney(totalReturn, currencySymbol)}</strong></div><div><span>Profit</span><strong>{formatSignedMoney(profit, currencySymbol)}</strong></div></section>
}

function isIncrementAligned(value: number, minimum: number, increment: number) {
  const steps = (value - minimum) / increment
  return Number.isFinite(steps) && Math.abs(steps - Math.round(steps)) < 1e-9
}
function shortRank(rank: CasinoWarCard['rank']) { return ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[rank] }
function suitSymbol(suit: CasinoWarCard['suit']) { return ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[suit] }
function humanize(value: string) { return value.split('-').map(word => word[0]?.toUpperCase() + word.slice(1)).join(' ') }
function primaryOutcomeLabel(round: CasinoWarRound) { return round.primarySettlement ? humanize(round.primarySettlement.outcome) : 'Round complete' }
function formatMoney(value: number, currencySymbol: string) { return `${value < 0 ? '-' : ''}${currencySymbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function formatSignedMoney(value: number, currencySymbol: string) { return `${value > 0 ? '+' : ''}${formatMoney(value, currencySymbol)}` }
function messageForError(reason: unknown) { return reason instanceof CasinoWarGatewayError ? reason.message : 'The Casino War table is unavailable.' }
function isTableUnavailable(reason: unknown) { return reason instanceof CasinoWarGatewayError && reason.code === 'casino-war-disabled' }
function createRequestKey(action: 'opening' | 'decision') { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `casino-war-${action}-${random}` }
function storedRoundKey(playerId: string) { return `fortuneforge:casino-war:round:${playerId}` }
function pendingActionKey(playerId: string) { return `fortuneforge:casino-war:pending:${playerId}` }
function readStoredRoundId(playerId: string) { try { return sessionStorage.getItem(storedRoundKey(playerId)) } catch { return null } }
function readPendingAction(playerId: string): StoredPendingAction | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingActionKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const action = value as Record<string, unknown>
    if (action.operation === 'opening' && typeof action.idempotencyKey === 'string' && typeof action.primaryStake === 'number' && typeof action.tieStake === 'number') {
      return { operation: 'opening', idempotencyKey: action.idempotencyKey, primaryStake: action.primaryStake, tieStake: action.tieStake }
    }
    if (action.operation === 'decision' && typeof action.idempotencyKey === 'string' && typeof action.roundId === 'string' && (action.decision === 'surrender' || action.decision === 'go-to-war')) {
      return { operation: 'decision', idempotencyKey: action.idempotencyKey, roundId: action.roundId, decision: action.decision }
    }
  } catch { /* session storage is optional */ }
  return null
}
function storeRoundId(playerId: string | undefined, roundId: string) { if (!playerId) return; try { sessionStorage.setItem(storedRoundKey(playerId), roundId) } catch { /* session storage is optional */ } }
function storePendingAction(playerId: string | undefined, action: StoredPendingAction) { if (!playerId) return; try { sessionStorage.setItem(pendingActionKey(playerId), JSON.stringify(action)) } catch { /* session storage is optional */ } }
function clearStoredRoundId(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(storedRoundKey(playerId)) } catch { /* session storage is optional */ } }
function clearPendingAction(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingActionKey(playerId)) } catch { /* session storage is optional */ } }

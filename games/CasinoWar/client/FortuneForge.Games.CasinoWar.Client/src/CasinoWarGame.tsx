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
import './casinoWarViewport.css'

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
  const [rulesOpen, setRulesOpen] = useState(false)
  const [lastBalance, setLastBalance] = useState<number | null>(null)
  const openingRequestKey = useRef<string | null>(null)
  const decisionRequestKey = useRef<string | null>(null)
  const pendingOpeningStakes = useRef<Readonly<{ primaryStake: number; tieStake: number }> | null>(null)
  const pendingDecision = useRef<CasinoWarDecision | null>(null)
  const rulesTrigger = useRef<HTMLButtonElement>(null)
  const rulesDialog = useRef<HTMLElement>(null)
  const rulesClose = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!round) { setRevealStage(0); return }
    if (round.phase === 'completed' && round.decision === 'surrender') { setRevealStage(2); return }
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
      setPrimaryStake(current => current >= nextStatus.minimumPrimaryStake && current <= nextStatus.maximumPrimaryStake && isIncrementAligned(current, nextStatus.minimumPrimaryStake, nextStatus.stakeIncrement) ? current : nextStatus.minimumPrimaryStake)
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
      if (pendingAction.operation === 'opening') {
        setPrimaryStake(pendingAction.primaryStake)
        setTieStake(pendingAction.tieStake)
      }
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
        setRecovery('ready')
        setError(null)
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
      setError(null)
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      if (reason instanceof CasinoWarGatewayError && reason.status === 404) {
        clearStoredRoundId(playerId)
        setRecovery('ready')
        setError(null)
        return
      }
      setRecovery('failed')
      setError('Your pending Casino War request could not be restored. Retry restoration before starting another round.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  useEffect(() => {
    if (!round) return
    setPrimaryStake(round.primaryStake)
    setTieStake(round.tieStake)
  }, [round])

  const balance = round?.balance ?? lastBalance ?? status?.balance ?? null
  const displayedBalance = lastBalance ?? status?.balance ?? null
  const hasWarCards = Boolean(round?.playerWarCard || round?.dealerWarCard)
  const revealed = round !== null && revealStage >= (hasWarCards ? 4 : 2)
  useEffect(() => {
    if (!round || !revealed) return
    setLastBalance(round.balance)
    onBalanceChange?.(round.balance)
  }, [onBalanceChange, revealed, round])

  useEffect(() => {
    if (!rulesOpen) return
    rulesClose.current?.focus()
    const closeRules = (returnFocus: boolean) => {
      setRulesOpen(false)
      if (returnFocus) window.requestAnimationFrame(() => rulesTrigger.current?.focus())
    }
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') closeRules(true) }
    const onPointerDown = (event: PointerEvent) => {
      if (!rulesDialog.current?.contains(event.target as Node) && event.target !== rulesTrigger.current) closeRules(false)
    }
    document.addEventListener('keydown', onKeyDown)
    document.addEventListener('pointerdown', onPointerDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.removeEventListener('pointerdown', onPointerDown)
    }
  }, [rulesOpen])

  const primaryIsValid = status !== null && primaryStake >= status.minimumPrimaryStake &&
    primaryStake <= status.maximumPrimaryStake && isIncrementAligned(primaryStake, status.minimumPrimaryStake, status.stakeIncrement)
  const tieIsValid = status !== null && tieStake >= 0 && tieStake <= status.maximumTieStake && Number.isFinite(tieStake) &&
    (tieStake === 0 || isIncrementAligned(tieStake, 0, status.stakeIncrement))
  const stakesAffordable = balance !== null && primaryStake + tieStake <= balance

  const act = async (operation: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try { await operation() } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }

  const deal = () => {
    if (!status?.available || busy || recovery !== 'ready' || (!pendingOpeningStakes.current && (!primaryIsValid || !tieIsValid || !stakesAffordable))) return
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
    })
  }

  const decide = (decision: CasinoWarDecision) => {
    if (!round || round.phase !== 'awaiting-tie-decision' || busy) return
    if (decision === 'go-to-war' && pendingDecision.current === null && round.primaryStake > round.balance) return
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
    if (!round || round.phase !== 'completed' || busy || recovery !== 'ready' || !status?.available || round.primaryStake + round.tieStake > round.balance) return
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
    })
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  return (
    <main className="ff-casino-war" aria-busy={busy}>
      <section className="ff-casino-war__table" aria-label="Casino War table">
        <div className="ff-casino-war__table-top">
          <button className="ff-casino-war__help" ref={rulesTrigger} type="button" aria-label="How to play Casino War" aria-expanded={rulesOpen} onClick={() => setRulesOpen(true)}>?</button>
        </div>

        <section className="ff-casino-war__arena" aria-live="polite">
          <CardSeat label="Dealer" openingLabel="Dealer opening card" openingCard={round && revealStage >= 2 ? round.dealerOpeningCard : null} warCard={round && revealStage >= 4 ? round.dealerWarCard : null} showBack={!round || revealStage < 2} />
          <RoundCallout round={round} revealStage={revealStage} currencySymbol={currencySymbol} />
          <CardSeat label="You" openingLabel="Player opening card" openingCard={round && revealStage >= 1 ? round.playerOpeningCard : null} warCard={round && revealStage >= 3 ? round.playerWarCard : null} showBack={!round || revealStage < 1} />
        </section>

        <section className="ff-casino-war__controls">
          <div className="ff-casino-war__balance"><span>Balance</span><strong>{displayedBalance === null ? '—' : formatMoney(displayedBalance, currencySymbol)}</strong></div>
          {!round && <>
            <StakeControl label="Main bet" value={primaryStake} min={status?.minimumPrimaryStake} max={status?.maximumPrimaryStake} step={status?.stakeIncrement} disabled={busy || recovery !== 'ready' || !status?.available || pendingOpeningStakes.current !== null} invalid={!primaryIsValid} onChange={setPrimaryStake} currencySymbol={currencySymbol} />
            <StakeControl label="Tie bet" value={tieStake} min={0} max={status?.maximumTieStake} step={status?.stakeIncrement} disabled={busy || recovery !== 'ready' || !status?.available || pendingOpeningStakes.current !== null} invalid={!tieIsValid} onChange={setTieStake} currencySymbol={currencySymbol} optional />
            <button className="ff-casino-war__primary ff-casino-war__deal" disabled={busy || recovery !== 'ready' || !status?.available || (!pendingOpeningStakes.current && (!primaryIsValid || !tieIsValid || !stakesAffordable))} onClick={deal}>{busy ? 'Dealing…' : pendingOpeningStakes.current ? 'Retry' : 'Deal'}</button>
          </>}
          {round?.phase === 'awaiting-tie-decision' && revealStage >= 2 && <div className="ff-casino-war__decision" aria-label="Tie decision">
            <button className="ff-casino-war__secondary" aria-label={pendingDecision.current === 'surrender' ? 'Retry Surrender' : 'Surrender'} disabled={busy || (pendingDecision.current !== null && pendingDecision.current !== 'surrender')} onClick={() => decide('surrender')}><span>{pendingDecision.current === 'surrender' ? 'Retry' : 'Surrender'}</span><small>Return {formatMoney(round.primaryStake / 2, currencySymbol)}</small></button>
            <button className="ff-casino-war__primary" aria-label={pendingDecision.current === 'go-to-war' ? 'Retry Go to War' : 'Go to War'} disabled={busy || (pendingDecision.current !== null && pendingDecision.current !== 'go-to-war') || (pendingDecision.current === null && round.primaryStake > round.balance)} onClick={() => decide('go-to-war')}><span>{busy ? 'Resolving…' : pendingDecision.current === 'go-to-war' ? 'Retry War' : 'Go to War'}</span><small>Add {formatMoney(round.primaryStake, currencySymbol)}</small></button>
          </div>}
          {round?.phase === 'completed' && revealStage >= ((round.playerWarCard || round.dealerWarCard) ? 4 : 2) && <div className="ff-casino-war__round-actions"><button className="ff-casino-war__secondary" disabled={busy} onClick={reset}>Change bet</button><button className="ff-casino-war__primary" disabled={busy || !status?.available || recovery !== 'ready' || round.primaryStake + round.tieStake > round.balance} onClick={rebet}>Deal again</button></div>}
          {recovery === 'recovering' && <span className="ff-casino-war__loading" role="status">Restoring…</span>}
          {recovery === 'failed' && <button className="ff-casino-war__secondary" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
          {!status && error && !tableUnavailable && <button className="ff-casino-war__secondary" type="button" onClick={retryStatus}>Retry connection</button>}
        </section>

        {rulesOpen && <section className="ff-casino-war__rules-dialog" ref={rulesDialog} role="dialog" aria-modal="true" aria-label="How to play Casino War">
          <button type="button" className="ff-casino-war__rules-close" ref={rulesClose} aria-label="Close rules" onClick={() => { setRulesOpen(false); window.requestAnimationFrame(() => rulesTrigger.current?.focus()) }}>×</button>
          <h2>How to play</h2>
          <dl>
            <div><dt>Main bet</dt><dd>Highest card wins and pays 1:1. Ace is high; suits do not matter.</dd></div>
            <div><dt>Tie bet</dt><dd>An opening tie pays 10:1 profit.</dd></div>
            <div><dt>Surrender</dt><dd>On a tie, recover half of the main bet.</dd></div>
            <div><dt>Go to War</dt><dd>Add one matching main bet. Three cards are burned before each War card. A second tie wins.</dd></div>
          </dl>
          {status && <p>Main bet {formatMoney(status.minimumPrimaryStake, currencySymbol)}–{formatMoney(status.maximumPrimaryStake, currencySymbol)} · Tie bet up to {formatMoney(status.maximumTieStake, currencySymbol)}</p>}
        </section>}
      </section>
      {error && <p className="ff-casino-war__error" role="alert">{error}</p>}
    </main>
  )
}

function StakeControl({ label, value, min = 0, max = Number.MAX_SAFE_INTEGER, step = 1, disabled, invalid, onChange, currencySymbol, optional = false }: Readonly<{ label: string; value: number; min?: number; max?: number; step?: number; disabled: boolean; invalid: boolean; onChange: (value: number) => void; currencySymbol: string; optional?: boolean }>) {
  const [draft, setDraft] = useState(Number.isFinite(value) ? String(value) : '')
  useEffect(() => { setDraft(Number.isFinite(value) ? String(value) : '') }, [value])
  const change = (next: number) => onChange(Math.min(max, Math.max(min, roundStake(next))))
  return <div className="ff-casino-war__stake">
    <span>{label}{optional && <small>Optional</small>}</span>
    <div><button type="button" aria-label={`Decrease ${label}`} disabled={disabled || !Number.isFinite(value) || value <= min} onClick={() => change(value - step)}>−</button><label><span>{currencySymbol}</span><input aria-label={label} type="number" value={draft} min={min} max={max} step={step} disabled={disabled} aria-invalid={draft !== '' && invalid} onChange={event => { setDraft(event.target.value); onChange(event.target.value === '' ? Number.NaN : Number(event.target.value)) }} /></label><button type="button" aria-label={`Increase ${label}`} disabled={disabled || !Number.isFinite(value) || value >= max} onClick={() => change(value + step)}>+</button></div>
  </div>
}

function CardSeat({ label, openingLabel, openingCard, warCard, showBack }: Readonly<{ label: string; openingLabel: string; openingCard: CasinoWarCard | null; warCard: CasinoWarCard | null; showBack: boolean }>) {
  return <section className={`ff-casino-war__seat ff-casino-war__seat--${label.toLowerCase()}`} aria-label={`${label} cards`}>
    <strong>{label}</strong>
    <div className="ff-casino-war__hand">
      {openingCard ? <div aria-label={openingLabel}><CardFace card={openingCard} /></div> : showBack ? <CardBack label={`${label} card`} /> : null}
      {warCard && <div className="ff-casino-war__war-card" aria-label={`${label} War card`}><CardFace card={warCard} /></div>}
    </div>
  </section>
}

function CardBack({ label }: Readonly<{ label: string }>) { return <div className="ff-casino-war__card ff-casino-war__card--back" aria-label={label}><span>FF</span></div> }

function CardFace({ card }: Readonly<{ card: CasinoWarCard }>) {
  const symbol = suitSymbol(card.suit)
  const red = card.suit === 'diamonds' || card.suit === 'hearts'
  return <div className={`ff-casino-war__card${red ? ' is-red' : ''}`} aria-label={`${card.rank} of ${card.suit}`}><span className="ff-casino-war__corner">{shortRank(card.rank)}<small>{symbol}</small></span><span className="ff-casino-war__suit" aria-hidden="true">{symbol}</span><span className="ff-casino-war__corner ff-casino-war__corner--reverse">{shortRank(card.rank)}<small>{symbol}</small></span></div>
}

function RoundCallout({ round, revealStage, currencySymbol }: Readonly<{ round: CasinoWarRound | null; revealStage: number; currencySymbol: string }>) {
  if (!round) return <div className="ff-casino-war__crest" aria-hidden="true"><span>W</span></div>
  if (round.phase === 'awaiting-tie-decision') return revealStage >= 2 ? <div className="ff-casino-war__callout is-tie"><strong>Tie</strong>{round.tieSettlement && <small>Tie bet {formatSignedMoney(round.tieSettlement.profit, currencySymbol)}</small>}</div> : null
  const neededStage = (round.playerWarCard || round.dealerWarCard) ? 4 : 2
  if (revealStage < neededStage) return null
  const net = (round.primarySettlement?.profit ?? 0) + (round.tieSettlement?.profit ?? 0)
  const title = round.primarySettlement?.disposition === 'surrender' ? 'Surrendered' : net > 0 ? 'You win' : net < 0 ? 'Dealer wins' : 'Push'
  return <div className={`ff-casino-war__callout is-${net > 0 ? 'win' : net < 0 ? 'loss' : 'push'}`}><strong>{title}</strong><b>{formatSignedMoney(net, currencySymbol)}</b>{round.tieSettlement && <small>Tie bet {formatSignedMoney(round.tieSettlement.profit, currencySymbol)}</small>}</div>
}

function isIncrementAligned(value: number, minimum: number, increment: number) {
  const steps = (value - minimum) / increment
  return Number.isFinite(steps) && Math.abs(steps - Math.round(steps)) < 1e-9
}
function shortRank(rank: CasinoWarCard['rank']) { return ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[rank] }
function suitSymbol(suit: CasinoWarCard['suit']) { return ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[suit] }
function humanize(value: string) { return value.split('-').map(word => word[0]?.toUpperCase() + word.slice(1)).join(' ') }
function formatMoney(value: number, currencySymbol: string) { return `${value < 0 ? '-' : ''}${currencySymbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function formatSignedMoney(value: number, currencySymbol: string) { return `${value > 0 ? '+' : ''}${formatMoney(value, currencySymbol)}` }
function roundStake(value: number) { return Math.round((value + Number.EPSILON) * 100) / 100 }
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

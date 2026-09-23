import { useEffect, useMemo, useRef, useState } from 'react'
import { SicBoGatewayError, type SicBoBetKind, type SicBoBetRequest, type SicBoGateway, type SicBoRound, type SicBoStatus } from './contracts'
import { HttpSicBoGateway } from './httpSicBoGateway'
import './sicBo.css'
import './sicBoGuide.css'

export type SicBoGameProps = Readonly<{
  gateway?: SicBoGateway
  playerId?: string
  currencySymbol?: string
  onBalanceChange?: (balance: number) => void
}>

const defaultGateway = new HttpSicBoGateway()
type StoredPendingRoll = Readonly<{ idempotencyKey: string; bets: readonly SicBoBetRequest[] }>
type RollHistory = Readonly<{ roundId: string; dice: readonly [number, number, number]; total: number; isTriple: boolean }>
const quickBets: readonly Readonly<{ kind: SicBoBetKind; label: string; hint: string }>[] = [
  { kind: 'small', label: 'Small', hint: 'Total 4–10' }, { kind: 'big', label: 'Big', hint: 'Total 11–17' },
  { kind: 'odd', label: 'Odd', hint: 'Odd total' }, { kind: 'even', label: 'Even', hint: 'Even total' },
  { kind: 'any-triple', label: 'Any Triple', hint: 'Any three alike' },
]
const configuredKinds: readonly Readonly<{ kind: SicBoBetKind; label: string }>[] = [
  { kind: 'single-number', label: 'Single Number' }, { kind: 'specific-double', label: 'Specific Double' },
  { kind: 'specific-triple', label: 'Specific Triple' }, { kind: 'total', label: 'Total' },
  { kind: 'two-number-combination', label: 'Two-number Combination' },
]

export function SicBoGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange }: SicBoGameProps) {
  const [status, setStatus] = useState<SicBoStatus | null>(null)
  const [currentBalance, setCurrentBalance] = useState<number | null>(null)
  const [slip, setSlip] = useState<readonly SicBoBetRequest[]>([])
  const [round, setRound] = useState<SicBoRound | null>(null)
  const [stake, setStake] = useState(1)
  const [kind, setKind] = useState<SicBoBetKind>('single-number')
  const [face, setFace] = useState(1)
  const [total, setTotal] = useState(4)
  const [firstFace, setFirstFace] = useState(1)
  const [secondFace, setSecondFace] = useState(2)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [lastSlip, setLastSlip] = useState<readonly SicBoBetRequest[]>([])
  const [history, setHistory] = useState<readonly RollHistory[]>(() => readRollHistory(playerId))
  const [revealedDice, setRevealedDice] = useState(0)
  const roundRequestKey = useRef<string | null>(null)
  const pendingSlip = useRef<readonly SicBoBetRequest[] | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setCurrentBalance(nextStatus.balance)
      setTableUnavailable(!nextStatus.available)
      setError(nextStatus.available ? null : 'This table is temporarily unavailable. Please choose another game.')
      setStake(nextStatus.minimumStake)
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      const unavailable = isTableUnavailable(reason)
      setTableUnavailable(unavailable)
      setError(unavailable ? 'This table is temporarily unavailable. Please choose another game.' : messageForError(reason))
    })
    return () => controller.abort()
  }, [gateway, statusLoadAttempt])

  useEffect(() => {
    const pendingRoll = playerId ? readPendingRoll(playerId) : null
    if (!pendingRoll) {
      setRecovery('ready')
      return undefined
    }
    const controller = new AbortController()
    setRecovery('recovering')
    void gateway.createRound(pendingRoll.bets, {
      signal: controller.signal,
      idempotencyKey: pendingRoll.idempotencyKey,
    }).then(nextRound => {
      clearPendingRoll(playerId)
      setRound(nextRound)
      setCurrentBalance(nextRound.balance)
      onBalanceChange?.(nextRound.balance)
      setRecovery('ready')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      setRecovery('failed')
      setError('Your pending Sic Bo roll could not be restored. Retry restoration before starting another round.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  useEffect(() => {
    setHistory(readRollHistory(playerId))
  }, [playerId])

  useEffect(() => {
    if (!round) { setRevealedDice(0); return }
    setHistory(current => {
      if (current.some(item => item.roundId === round.roundId)) return current
      const next = [{ roundId: round.roundId, dice: round.dice, total: round.total, isTriple: round.isTriple }, ...current].slice(0, 12)
      writeRollHistory(playerId, next)
      return next
    })
    setRevealedDice(0)
    const timers = [0, 1, 2].map(index => window.setTimeout(() => setRevealedDice(index + 1), 220 + index * 260))
    return () => timers.forEach(timer => window.clearTimeout(timer))
  }, [playerId, round])

  const balance = currentBalance ?? round?.balance ?? status?.balance ?? null
  const totalStake = useMemo(() => slip.reduce((sum, bet) => sum + bet.stake, 0), [slip])
  const remainingBalance = balance === null ? null : balance - totalStake
  const locked = busy || recovery !== 'ready' || round !== null || !status?.available || pendingSlip.current !== null
  const stakeValid = status !== null && isStakeValid(stake, status)
  const draft = createBet(kind, stake, face, total, firstFace, secondFace)
  const canAdd = !locked && stakeValid && draft !== null && slip.length < status.maximumBetsPerRound &&
    remainingBalance !== null && stake <= remainingBalance + 1e-9
  const canRoll = recovery === 'ready' && !busy && round === null && status?.available === true && slip.length > 0 && remainingBalance !== null && remainingBalance >= -1e-9

  const addBet = (nextKind = kind) => {
    if (locked || !stakeValid || !status) return
    const nextBet = createBet(nextKind, stake, face, total, firstFace, secondFace)
    if (!nextBet || slip.length >= status.maximumBetsPerRound || nextBet.stake > (remainingBalance ?? 0) + 1e-9) return
    roundRequestKey.current = null
    setSlip(current => [...current, nextBet])
    setError(null)
  }

  const roll = () => {
    if (!canRoll || busy) return
    const idempotencyKey = roundRequestKey.current ??= createRequestKey()
    const bets = pendingSlip.current ?? slip
    setLastSlip(bets)
    pendingSlip.current = bets
    storePendingRoll(playerId, { idempotencyKey, bets })
    setBusy(true)
    setError(null)
    void gateway.createRound(bets, { idempotencyKey }).then(nextRound => {
      roundRequestKey.current = null
      pendingSlip.current = null
      clearPendingRoll(playerId)
      setRound(nextRound)
      setCurrentBalance(nextRound.balance)
      onBalanceChange?.(nextRound.balance)
    }).catch(reason => setError(messageForError(reason))).finally(() => setBusy(false))
  }

  const newRound = (repeat = false) => {
    if (busy) return
    setRound(null)
    setSlip(repeat ? lastSlip : [])
    setError(null)
    roundRequestKey.current = null
    pendingSlip.current = null
    clearPendingRoll(playerId)
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  return <main className="ff-sic-bo" aria-busy={busy}>
    <header className="ff-sic-bo__header">
      <div><span className="ff-sic-bo__eyebrow">Three dice · nine ways to play</span><h1>Sic Bo</h1></div>
      <div className="ff-sic-bo__account"><span>Balance</span><strong>{balance === null ? '—' : formatMoney(balance, currencySymbol)}</strong><small>{status?.available ? 'Table open' : tableUnavailable || status ? 'Table unavailable' : 'Connecting…'}</small></div>
    </header>

    {status && <section className="ff-sic-bo__limits" aria-label="Table limits">
      <span>Min {formatMoney(status.minimumStake, currencySymbol)}</span><span>Max {formatMoney(status.maximumStakePerBet, currencySymbol)} per bet</span>
      <span>Step {formatMoney(status.stakeIncrement, currencySymbol)}</span><span>{status.maximumBetsPerRound} bets per round</span><span>{status.mode}</span>
    </section>}

    {!status && !error && <p className="ff-sic-bo__state" role="status">Loading Sic Bo table…</p>}
    {!status && error && !tableUnavailable && <button className="ff-sic-bo__primary" type="button" onClick={retryStatus}>Retry connection</button>}
    {status && !status.available && <p className="ff-sic-bo__state" role="status">This Sic Bo table is currently unavailable.</p>}

    {status?.available && !round && <section className="ff-sic-bo__layout" aria-label="Sic Bo betting table">
      <section className="ff-sic-bo__builder">
        <div className="ff-sic-bo__section-heading"><div><span>Build a wager</span><h2>Place your bets</h2></div><p>Triples do not count for Small, Big, Odd, or Even.</p></div>
        <details className="ff-sic-bo__bet-guide">
          <summary>Bet guide <span>Profit odds</span></summary>
          <dl>
            <div><dt>Small / Big / Odd / Even</dt><dd>1:1</dd></div>
            <div><dt>Single Number <small>1 / 2 / 3 appearances</small></dt><dd>1:1 / 2:1 / 12:1</dd></div>
            <div><dt>Two-number Combination</dt><dd>6:1</dd></div>
            <div><dt>Specific Double</dt><dd>11.5:1</dd></div>
            <div><dt>Any Triple</dt><dd>32:1</dd></div>
            <div><dt>Specific Triple</dt><dd>195:1</dd></div>
            <div><dt>Total 4 / 17</dt><dd>64:1</dd></div>
            <div><dt>Totals 5–16</dt><dd>32:1 to 6.5:1</dd></div>
          </dl>
          <p>Winning bets return the stake plus the listed profit. Total odds depend on the selected total.</p>
        </details>
        <label className="ff-sic-bo__stake">Stake
          <input aria-label="Stake" type="number" value={stake} min={status.minimumStake} max={status.maximumStakePerBet} step={status.stakeIncrement} disabled={locked} onChange={event => setStake(Number(event.target.value))} />
          <small>Min {formatMoney(status.minimumStake, currencySymbol)} · step {formatMoney(status.stakeIncrement, currencySymbol)}</small>
        </label>
        <section className="ff-sic-bo__quick" aria-label="Quick bets"><span>Quick bets</span><div>{quickBets.map(bet => <button key={bet.kind} type="button" title={bet.hint} disabled={!canQuickAdd(locked, stakeValid, stake, slip, status, remainingBalance)} onClick={() => addBet(bet.kind)}>{bet.label}<small>{bet.hint}</small></button>)}</div></section>
        <section className="ff-sic-bo__configured" aria-label="Configured bet">
          <label>Bet type<select aria-label="Bet type" value={kind} disabled={locked} onChange={event => setKind(event.target.value as SicBoBetKind)}>{configuredKinds.map(option => <option value={option.kind} key={option.kind}>{option.label}</option>)}</select></label>
          {kind === 'total' && <FaceSelect label="Total" value={total} min={4} max={17} disabled={locked} onChange={setTotal} />}
          {kind !== 'total' && kind !== 'two-number-combination' && <FaceSelect label="Face" value={face} min={1} max={6} disabled={locked} onChange={setFace} />}
          {kind === 'two-number-combination' && <div className="ff-sic-bo__pair"><FaceSelect label="First face" value={firstFace} min={1} max={6} disabled={locked} onChange={setFirstFace} /><FaceSelect label="Second face" value={secondFace} min={1} max={6} disabled={locked} onChange={setSecondFace} /></div>}
          {kind === 'two-number-combination' && firstFace === secondFace && <small className="ff-sic-bo__hint">Choose two different faces.</small>}
          <button className="ff-sic-bo__secondary" type="button" disabled={!canAdd} onClick={() => addBet()}>Add Bet</button>
        </section>
        {!stakeValid && <p className="ff-sic-bo__validation">Use a stake from {formatMoney(status.minimumStake, currencySymbol)} to {formatMoney(status.maximumStakePerBet, currencySymbol)} in {formatMoney(status.stakeIncrement, currencySymbol)} steps.</p>}
      </section>

      <Slip slip={slip} totalStake={totalStake} balance={balance} locked={locked} busy={busy} retryPending={pendingSlip.current !== null} maxBets={status.maximumBetsPerRound} currencySymbol={currencySymbol} onRemove={index => { roundRequestKey.current = null; setSlip(current => current.filter((_, currentIndex) => currentIndex !== index)) }} onClear={() => { roundRequestKey.current = null; setSlip([]) }} onRoll={roll} canRoll={canRoll} />
    </section>}

    {recovery === 'recovering' && <p className="ff-sic-bo__state" role="status">Restoring your pending Sic Bo roll…</p>}
    {recovery === 'failed' && <button className="ff-sic-bo__primary" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}

    {round && <SettledRound round={round} currencySymbol={currencySymbol} onNewRound={() => newRound(false)} onRepeat={() => newRound(true)} canRepeat={lastSlip.length > 0 && lastSlip.reduce((sum, bet) => sum + bet.stake, 0) <= round.balance + 1e-9} disabled={busy} revealedDice={revealedDice} history={history} />}
    {error && <p className="ff-sic-bo__error" role="alert">{error}</p>}
  </main>
}

function Slip({ slip, totalStake, balance, locked, busy, retryPending, maxBets, currencySymbol, onRemove, onClear, onRoll, canRoll }: Readonly<{ slip: readonly SicBoBetRequest[]; totalStake: number; balance: number | null; locked: boolean; busy: boolean; retryPending: boolean; maxBets: number; currencySymbol: string; onRemove: (index: number) => void; onClear: () => void; onRoll: () => void; canRoll: boolean }>) {
  return <aside className="ff-sic-bo__slip" aria-label="Bet slip"><div className="ff-sic-bo__section-heading"><div><span>Round slip</span><h2>{slip.length} / {maxBets} bets</h2></div>{slip.length > 0 ? <button className="ff-sic-bo__clear" type="button" disabled={locked} onClick={onClear}>Clear all</button> : <p>Add one or more bets to roll.</p>}</div>
    {slip.length === 0 ? <p className="ff-sic-bo__empty">Your bet slip is empty.</p> : <ol>{slip.map((bet, index) => <li key={`${bet.kind}-${index}`}><span><strong>{describeBet(bet)}</strong><small>{formatMoney(bet.stake, currencySymbol)}</small></span><button type="button" aria-label={`Remove ${describeBet(bet)}`} disabled={locked} onClick={() => onRemove(index)}>Remove</button></li>)}</ol>}
    <dl><div><dt>Total stake</dt><dd>{formatMoney(totalStake, currencySymbol)}</dd></div><div><dt>Remaining balance</dt><dd>{balance === null ? '—' : formatMoney(balance - totalStake, currencySymbol)}</dd></div></dl>
    <button className="ff-sic-bo__primary" type="button" disabled={!canRoll} onClick={onRoll}>{busy ? 'Rolling…' : retryPending ? 'Retry Roll' : 'Roll Dice'}</button>
  </aside>
}

function SettledRound({ round, currencySymbol, onNewRound, onRepeat, canRepeat, disabled, revealedDice, history }: Readonly<{ round: SicBoRound; currencySymbol: string; onNewRound: () => void; onRepeat: () => void; canRepeat: boolean; disabled: boolean; revealedDice: number; history: readonly RollHistory[] }>) {
  const smallCount = history.filter(item => !item.isTriple && item.total <= 10).length
  const bigCount = history.filter(item => !item.isTriple && item.total >= 11).length
  return <section className="ff-sic-bo__result" aria-live="polite"><div className="ff-sic-bo__result-top"><div><span>{revealedDice < 3 ? 'Dice tumbling' : 'Round settled'}</span><h2>{revealedDice < 3 ? 'Revealing…' : round.isTriple ? 'Triple rolled' : `Total ${round.total}`}</h2><p>{revealedDice < 3 ? 'Each die is revealed before bets settle.' : round.isTriple ? `All dice show ${round.dice[0]}.` : 'Authoritative results returned by the table.'}</p></div><div className="ff-sic-bo__dice" aria-label={`Dice: ${round.dice.join(', ')}`}>{round.dice.map((value, index) => index < revealedDice ? <Die key={index} value={value} /> : <span className="ff-sic-bo__die is-rolling" key={index} aria-label="Die rolling">?</span>)}</div></div>
    {revealedDice === 3 && <section className="ff-sic-bo__settlements" aria-label="Round settlements">{round.settlements.map(settlement => <article key={settlement.betIndex} className={settlement.won ? 'is-win' : 'is-loss'}><div><span>Bet {settlement.betIndex + 1}</span><strong>{describeBet(settlement)}</strong></div><div><span>Stake</span><strong>{formatMoney(settlement.stake, currencySymbol)}</strong></div><div><span>Outcome</span><strong>{settlement.won ? 'Win' : 'Loss'}</strong><small>{settlement.won ? `${settlement.profitOdds}:1 odds` : 'No return'}</small></div><div><span>Return</span><strong>{formatMoney(settlement.totalReturn, currencySymbol)}</strong></div></article>)}</section>}
    <section className="ff-sic-bo__totals" aria-label="Round totals"><div><span>Total staked</span><strong>{formatMoney(round.totalStaked, currencySymbol)}</strong></div><div><span>Total return</span><strong>{formatMoney(round.totalReturn, currencySymbol)}</strong></div><div><span>Net profit/loss</span><strong className={round.profit >= 0 ? 'is-win' : 'is-loss'}>{formatSignedMoney(round.profit, currencySymbol)}</strong></div><div><span>Updated balance</span><strong>{formatMoney(round.balance, currencySymbol)}</strong></div></section>
    <section className="ff-sic-bo__history" aria-label="Recent roll history"><div><small>Last {history.length} rolls</small><strong>Small {smallCount} · Big {bigCount} · Triples {history.filter(item => item.isTriple).length}</strong></div><ol>{history.map(item => <li key={item.roundId} className={item.isTriple ? 'is-triple' : item.total <= 10 ? 'is-small' : 'is-big'} title={item.dice.join(' + ')}>{item.total}</li>)}</ol></section>
    <div className="ff-sic-bo__result-actions"><button className="ff-sic-bo__secondary" type="button" disabled={disabled || !canRepeat} onClick={onRepeat}>Repeat Bets</button><button className="ff-sic-bo__primary" type="button" disabled={disabled} onClick={onNewRound}>New Round</button></div>
  </section>
}

function Die({ value }: Readonly<{ value: number }>) { return <span className="ff-sic-bo__die" aria-label={`Die ${value}`}>{Array.from({ length: value }, (_, index) => <i key={index} />)}</span> }
function FaceSelect({ label, value, min, max, disabled, onChange }: Readonly<{ label: string; value: number; min: number; max: number; disabled: boolean; onChange: (value: number) => void }>) { return <label>{label}<select aria-label={label} value={value} disabled={disabled} onChange={event => onChange(Number(event.target.value))}>{Array.from({ length: max - min + 1 }, (_, index) => min + index).map(option => <option key={option} value={option}>{option}</option>)}</select></label> }

function createBet(kind: SicBoBetKind, stake: number, face: number, total: number, firstFace: number, secondFace: number): SicBoBetRequest | null {
  const empty = { face: null, total: null, firstFace: null, secondFace: null }
  if (kind === 'small' || kind === 'big' || kind === 'odd' || kind === 'even' || kind === 'any-triple') return { kind, stake, ...empty }
  if (kind === 'total') return { kind, stake, face: null, total, firstFace: null, secondFace: null }
  if (kind === 'two-number-combination') {
    if (firstFace === secondFace) return null
    return { kind, stake, face: null, total: null, firstFace: Math.min(firstFace, secondFace), secondFace: Math.max(firstFace, secondFace) }
  }
  return { kind, stake, face, total: null, firstFace: null, secondFace: null }
}
function canQuickAdd(locked: boolean, stakeValid: boolean, stake: number, slip: readonly SicBoBetRequest[], status: SicBoStatus, remainingBalance: number | null) { return !locked && stakeValid && slip.length < status.maximumBetsPerRound && remainingBalance !== null && stake <= remainingBalance + 1e-9 }
function isStakeValid(value: number, status: SicBoStatus) { const steps = (value - status.minimumStake) / status.stakeIncrement; return Number.isFinite(value) && value >= status.minimumStake && value <= status.maximumStakePerBet && Math.abs(steps - Math.round(steps)) < 1e-9 }
function describeBet(bet: SicBoBetRequest) { switch (bet.kind) { case 'single-number': return `Single ${bet.face}`; case 'specific-double': return `Double ${bet.face}`; case 'specific-triple': return `Triple ${bet.face}`; case 'total': return `Total ${bet.total}`; case 'two-number-combination': return `Combination ${bet.firstFace} + ${bet.secondFace}`; case 'any-triple': return 'Any Triple'; default: return bet.kind[0]!.toUpperCase() + bet.kind.slice(1) } }
function formatMoney(value: number, currencySymbol: string) { return `${value < 0 ? '-' : ''}${currencySymbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function formatSignedMoney(value: number, currencySymbol: string) { return `${value > 0 ? '+' : ''}${formatMoney(value, currencySymbol)}` }
function messageForError(reason: unknown) { return reason instanceof SicBoGatewayError ? reason.message : 'The Sic Bo table is unavailable. Please try again.' }
function isTableUnavailable(reason: unknown) { return reason instanceof SicBoGatewayError && reason.code === 'sic-bo-disabled' }
function createRequestKey() { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `sic-bo-${random}` }
function pendingRollKey(playerId: string) { return `fortuneforge:sic-bo:pending:${playerId}` }
function readPendingRoll(playerId: string): StoredPendingRoll | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingRollKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const roll = value as Record<string, unknown>
    if (typeof roll.idempotencyKey === 'string' && Array.isArray(roll.bets) && roll.bets.length > 0 && roll.bets.every(isStoredBet)) {
      return { idempotencyKey: roll.idempotencyKey, bets: roll.bets }
    }
  } catch { /* session storage is optional */ }
  return null
}
function isStoredBet(value: unknown): value is SicBoBetRequest {
  if (!value || typeof value !== 'object') return false
  const bet = value as Record<string, unknown>
  return typeof bet.kind === 'string' && isSicBoBetKind(bet.kind) && typeof bet.stake === 'number' && Number.isFinite(bet.stake) &&
    [bet.face, bet.total, bet.firstFace, bet.secondFace].every(option => option === null || (typeof option === 'number' && Number.isFinite(option)))
}
function isSicBoBetKind(value: string): value is SicBoBetKind { return ['small', 'big', 'odd', 'even', 'single-number', 'total', 'two-number-combination', 'specific-double', 'any-triple', 'specific-triple'].includes(value) }
function storePendingRoll(playerId: string | undefined, roll: StoredPendingRoll) { if (!playerId) return; try { sessionStorage.setItem(pendingRollKey(playerId), JSON.stringify(roll)) } catch { /* session storage is optional */ } }
function clearPendingRoll(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingRollKey(playerId)) } catch { /* session storage is optional */ } }
function rollHistoryKey(playerId: string | undefined) { return `fortuneforge:sic-bo:history:${playerId ?? 'guest'}` }
function readRollHistory(playerId: string | undefined): readonly RollHistory[] { try { const value = JSON.parse(localStorage.getItem(rollHistoryKey(playerId)) ?? '[]'); return Array.isArray(value) ? value.filter(isRollHistory).slice(0, 12) : [] } catch { return [] } }
function writeRollHistory(playerId: string | undefined, history: readonly RollHistory[]): void { try { localStorage.setItem(rollHistoryKey(playerId), JSON.stringify(history)) } catch { /* storage is optional */ } }
function isRollHistory(value: unknown): value is RollHistory { if (!value || typeof value !== 'object') return false; const item = value as Record<string, unknown>; return typeof item.roundId === 'string' && Array.isArray(item.dice) && item.dice.length === 3 && item.dice.every(die => Number.isInteger(die) && die >= 1 && die <= 6) && Number.isInteger(item.total) && typeof item.isTriple === 'boolean' }

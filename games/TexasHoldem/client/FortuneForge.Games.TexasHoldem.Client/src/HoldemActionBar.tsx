import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import type { HoldemAction, HoldemTable } from './contracts'
import { formatHoldemCents } from './holdemMoney'

export type HoldemActionBarProps = Readonly<{
  table: HoldemTable
  busy: boolean
  onAction: (action: HoldemAction, raiseTo?: number) => void
  onLeave: () => void
}>

export function HoldemActionBar({ table, busy, onAction, onLeave }: HoldemActionBarProps) {
  const viewer = table.seats.find(seat => seat.isCurrentPlayer)
  const turn = table.status === 'active' && viewer?.seat === table.activeSeat && viewer.status === 'active'
  const maximum = Math.max(0, Math.round(table.maximumRaiseTo))
  const minimum = Math.min(maximum, Math.max(0, Math.round(table.shortAllInRaiseTo ?? table.minimumRaiseTo)))
  const [raiseTo, setRaiseTo] = useState(minimum)
  const [now, setNow] = useState(() => Date.now())
  const [confirmLeave, setConfirmLeave] = useState(false)
  const canAct = turn && !busy && !confirmLeave
  const leaveRef = useRef<HTMLButtonElement>(null)
  const stayRef = useRef<HTMLButtonElement>(null)
  const confirmTitle = useId()
  const confirmDescription = useId()
  const raiseInputId = useId()
  const fallbackDeadline = useMemo(() => Date.now() + table.remainingActionMilliseconds,
    [table.handNumber, table.street, table.activeSeat, table.remainingActionMilliseconds])
  const deadline = table.actionDeadlineAtUtc ? Date.parse(table.actionDeadlineAtUtc) : fallbackDeadline
  const seconds = Math.max(0, Math.min(Math.ceil(table.remainingActionMilliseconds / 1000), Math.ceil(((Number.isFinite(deadline) ? deadline : fallbackDeadline) - now) / 1000)))
  const callAmount = viewer ? Math.max(0, Math.min(table.currentBet - viewer.committedRound, viewer.stack)) : 0
  const step = Math.max(1, Math.round((table.tableRule?.bigBlindCredits ?? 1) * 100))
  const clamp = (value: number) => Math.max(minimum, Math.min(maximum, Number.isFinite(value) ? Math.round(value) : minimum))
  const target = clamp(raiseTo)
  const changeRaise = (value: number) => { if (canAct && table.legalActions.includes('raise')) setRaiseTo(clamp(value)) }
  const act = (action: HoldemAction, amount?: number) => { if (canAct && table.legalActions.includes(action)) onAction(action, amount) }

  useEffect(() => setRaiseTo(minimum), [minimum, maximum, table.handNumber, table.street])
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 250); return () => window.clearInterval(timer) }, [])
  useEffect(() => {
    if (!confirmLeave) return
    stayRef.current?.focus()
    return () => leaveRef.current?.focus()
  }, [confirmLeave])
  const requestLeave = () => {
    if (busy) return
    if (table.status === 'active') setConfirmLeave(true)
    else onLeave()
  }
  const dialogKeys = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'Escape') { event.preventDefault(); setConfirmLeave(false) }
    if (event.key !== 'Tab') return
    const controls = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)'))
    const first = controls[0], last = controls.at(-1)
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }
  return <>
    <section className="ff-holdem-actions" aria-label="Poker actions">
      {(!turn || busy) && <div className="ff-holdem-waiting" role="status">{busy ? 'Submitting move…' : table.status === 'active' ? 'Waiting for your turn' : 'Hand complete'}</div>}
      {turn && <div className="ff-holdem-turn-clock" role="timer" aria-label={`Time to act: ${seconds} seconds`}><span>Time to act</span><strong>{seconds}</strong></div>}
      <div className="ff-holdem-round-actions">
        {table.legalActions.includes('fold') && <button type="button" className="ff-holdem-action ff-holdem-action--fold" disabled={!canAct} onClick={() => act('fold')}><span>Fold</span></button>}
        {table.legalActions.includes('check') && <button type="button" className="ff-holdem-action ff-holdem-action--call" disabled={!canAct} onClick={() => act('check')}><span>Check</span></button>}
        {table.legalActions.includes('call') && <button type="button" className="ff-holdem-action ff-holdem-action--call" data-card-audio="chip" disabled={!canAct} onClick={() => act('call')}><span>Call</span> <small>{formatHoldemCents(callAmount)}</small></button>}
        {table.legalActions.includes('raise') && <button type="button" className="ff-holdem-action ff-holdem-action--raise ff-holdem-primary" data-card-audio="chip" disabled={!canAct} onClick={() => act('raise', target)}><span>Raise</span> <small>{formatHoldemCents(target)}</small></button>}
      </div>
      {table.legalActions.includes('raise') && <div className="ff-holdem-raise-control">
        <label htmlFor={raiseInputId}>Raise to</label>
        <input id={raiseInputId} type="range" aria-label="Raise amount" aria-valuetext={formatHoldemCents(target)} min={minimum} max={maximum} step={1} value={target} disabled={!canAct || minimum === maximum} onChange={event => changeRaise(Number(event.currentTarget.value))} />
        <div className="ff-holdem-raise-buttons"><button type="button" aria-label="Decrease raise" disabled={!canAct || target <= minimum} onClick={() => changeRaise(target - step)}>−</button>
          <output htmlFor={raiseInputId} aria-label="Selected raise amount">{formatHoldemCents(target)}</output>
          <button type="button" aria-label="Increase raise" disabled={!canAct || target >= maximum} onClick={() => changeRaise(target + step)}>+</button></div>
        <div className="ff-holdem-raise-shortcuts" aria-label="Raise shortcuts"><button type="button" disabled={!canAct} onClick={() => changeRaise(table.currentBet + Math.round(table.pot / 2))}>½ pot</button><button type="button" disabled={!canAct} onClick={() => changeRaise(table.currentBet + table.pot)}>Pot</button><button type="button" disabled={!canAct} onClick={() => changeRaise(maximum)}>All in</button></div>
      </div>}
      <button ref={leaveRef} type="button" className="ff-holdem-leave" disabled={busy || confirmLeave} onClick={requestLeave}>Leave table</button>
    {confirmLeave && <section className="ff-holdem-leave-confirm" role="alertdialog" aria-modal="true" aria-labelledby={confirmTitle} aria-describedby={confirmDescription} onKeyDown={dialogKeys}>
      <h2 id={confirmTitle}>Leave the table?</h2><p id={confirmDescription}>This hand is still in progress. Leaving now can fold your hand; chips already in the pot remain committed.</p>
      <div className="ff-holdem-confirm-actions"><button ref={stayRef} type="button" className="ff-holdem-primary" onClick={() => setConfirmLeave(false)}>Stay at table</button><button type="button" disabled={busy} onClick={() => { if (!busy) { setConfirmLeave(false); onLeave() } }}>Leave table</button></div>
    </section>}
    </section>
  </>
}

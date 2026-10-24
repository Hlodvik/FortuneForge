import { useEffect, useId, useRef } from 'react'
import type { HoldemTableRule } from './contracts'
import { formatHoldemCredits } from './holdemMoney'

export type HoldemLobbyProps = Readonly<{
  rules: readonly HoldemTableRule[]
  selectedRuleId: string
  balanceCredits: number
  busy: boolean
  buyInAmount: string
  onBuyInAmountChange: (amount: string) => void
  onSelectRule: (id: string) => void
  onJoin: (rule: HoldemTableRule, buyInCredits: number) => void
}>

export function HoldemLobby({ rules, selectedRuleId, balanceCredits, busy, buyInAmount, onBuyInAmountChange, onSelectRule, onJoin }: HoldemLobbyProps) {
  const selected = rules.find(rule => rule.id === selectedRuleId) ?? rules[0]
  const minimum = Math.ceil((selected?.minimumBuyInCredits ?? selected?.bigBlindCredits ?? 0) * 100 - .000001)
  const maximum = Math.max(0, Math.floor(Math.min(balanceCredits, selected?.maximumTableStackCredits ?? 0) * 100 + .000001))
  const affordable = selected !== undefined && maximum >= minimum && minimum > 0
  const cents = parseBuyInCents(buyInAmount)
  const valid = affordable && cents !== null && cents >= minimum && cents <= maximum
  const amountId = useId(), hintId = useId()
  const previousBounds = useRef({ ruleId: selected?.id, minimum, maximum })
  useEffect(() => {
    const previous = previousBounds.current
    previousBounds.current = { ruleId: selected?.id, minimum, maximum }
    if (busy) return
    if (previous.ruleId !== selected?.id) {
      onBuyInAmountChange(affordable ? (maximum / 100).toFixed(2) : '')
    } else if (previous.minimum !== minimum || previous.maximum !== maximum) {
      onBuyInAmountChange(affordable && cents !== null
        ? (Math.max(minimum, Math.min(maximum, cents)) / 100).toFixed(2) : affordable ? (maximum / 100).toFixed(2) : '')
    }
  }, [affordable, busy, cents, maximum, minimum, onBuyInAmountChange, selected?.id])
  const presets = [...new Set([minimum, Math.max(minimum, Math.round(maximum / 2)), maximum])]
  return <section className="ff-holdem-lobby" aria-label="Choose a Texas Hold’em table">
    <header className="ff-holdem-lobby-header"><span className="ff-holdem-lobby__eyebrow">No-limit poker</span><h1>Texas Hold’em</h1><p>Find your seat at the table.</p></header>
    <div className="ff-holdem-lobby__details"><span>Account balance</span><strong>{formatHoldemCredits(balanceCredits)}</strong></div>
    <div className="ff-holdem-stakes" role="radiogroup" aria-label="Table stakes">
      {rules.map(rule => <button type="button" role="radio" aria-checked={rule.id === selected?.id} disabled={busy}
        className={`ff-holdem-lobby-rule${rule.id === selected?.id ? ' is-selected' : ''}`} key={rule.id}
        onClick={() => { if (!busy) onSelectRule(rule.id) }}>
        <strong>{rule.name}</strong>
        <span>{formatHoldemCredits(rule.smallBlindCredits)} / {formatHoldemCredits(rule.bigBlindCredits)}</span>
        <small>{rule.description}</small>
        <small>Buy-in up to {formatHoldemCredits(rule.maximumTableStackCredits)}{rule.anteCredits > 0 ? ` · Ante ${formatHoldemCredits(rule.anteCredits)}` : ''}</small>
      </button>)}
    </div>
    {!selected && <p role="status">No tables are available right now.</p>}
    {selected && !affordable && <p className="ff-holdem-lobby-note">You need at least {formatHoldemCredits(selected.bigBlindCredits)} to take a seat.</p>}
    {selected && <div className="ff-holdem-buy-in">
      <label htmlFor={amountId}>Table chips to buy in</label>
      <div className="ff-holdem-buy-in__amount"><span aria-hidden="true">R</span><input id={amountId} type="text" inputMode="decimal" autoComplete="off" value={buyInAmount} disabled={busy || !affordable} aria-describedby={hintId} aria-invalid={buyInAmount !== '' && !valid}
        onChange={event => { if (!busy) onBuyInAmountChange(event.currentTarget.value) }}
        onBlur={() => { if (!busy) onBuyInAmountChange(valid && cents !== null ? (cents / 100).toFixed(2) : '') }}/></div>
      <input type="range" aria-label="Buy-in amount" aria-valuetext={valid && cents !== null ? formatHoldemCredits(cents / 100) : 'Choose an amount'} min={minimum} max={Math.max(minimum, maximum)} step={1} value={valid && cents !== null ? cents : minimum} disabled={busy || !affordable || minimum === maximum}
        onChange={event => { if (!busy) onBuyInAmountChange((Number(event.currentTarget.value) / 100).toFixed(2)) }}/>
      <div className="ff-holdem-buy-in__presets">{presets.map((amount, index) => <button key={amount} type="button" disabled={busy || !affordable} onClick={() => { if (!busy) onBuyInAmountChange((amount / 100).toFixed(2)) }}>{index === 0 ? 'Min' : index === presets.length - 1 ? 'Max' : 'Half'} <small>{formatHoldemCredits(amount / 100)}</small></button>)}</div>
      <p id={hintId}>{affordable ? `${formatHoldemCredits(minimum / 100)}–${formatHoldemCredits(maximum / 100)} · Whole cents only.` : 'Your account needs enough for the minimum buy-in.'} Moved from your account to table chips. Unspent chips return when you leave.</p>
    </div>}
    <button type="button" className="ff-holdem-lobby-join ff-holdem-primary" disabled={busy || !valid}
      onClick={() => { if (!busy && selected && valid && cents !== null) onJoin(selected, cents / 100) }}>{busy ? 'Finding a seat…' : !selected ? 'No tables available' : valid && cents !== null ? `Join ${selected.name} · ${formatHoldemCredits(cents / 100)}` : 'Choose a buy-in'}</button>
  </section>
}

function parseBuyInCents(value: string): number | null {
  if (!/^(?:\d+(?:\.\d{0,2})?|\.\d{1,2})$/.test(value.trim())) return null
  const amount = Math.round(Number(value) * 100)
  return Number.isSafeInteger(amount) && amount > 0 ? amount : null
}

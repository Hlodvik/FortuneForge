import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import type { CrapsExtraBetRequest, CrapsGateway, CrapsRound } from './contracts'
import { DiceThrow } from './DiceThrow'
import { availableChipValues, extraBetLabel, extraBetOptions, oddsCopy, pointNumbers, resultLabel, rollResultLabel, roundTotals, validStake } from './crapsPresentation'
import { useCrapsTable } from './useCrapsTable'
import './craps.css'
import './crapsEnhancements.css'
import './crapsViewport.css'

export type CrapsGameProps = Readonly<{
  gateway: CrapsGateway; playerId?: string; showTitle?: boolean; tableArtworkUrl?: string
  isYourTurn?: boolean; activePlayerName?: string; onRoundChange?: (round: CrapsRound | null) => void
}>
type Panel = 'tips' | 'history' | 'bets' | 'odds' | null
const money = (value: number) => 'R' + value.toFixed(2)
const chip = (value: number) => 'R' + Number(value.toFixed(2))

export function CrapsGame({ gateway, playerId, showTitle = true, tableArtworkUrl, isYourTurn = true, activePlayerName = 'Another player', onRoundChange }: CrapsGameProps) {
  const table = useCrapsTable(gateway, playerId, isYourTurn, onRoundChange)
  const { status, round, busy, motion, pendingRound, error, recovery } = table
  const [stake, setStake] = useState('10')
  const [extraStake, setExtraStake] = useState('5')
  const [oddsStake, setOddsStake] = useState('10')
  const [selectedExtras, setSelectedExtras] = useState<readonly CrapsExtraBetRequest['kind'][]>([])
  const [draftExtras, setDraftExtras] = useState<readonly CrapsExtraBetRequest[] | null>(null)
  const [panel, setPanel] = useState<Panel>(null)
  const trigger = useRef<HTMLButtonElement | null>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const primary = useRef<HTMLButtonElement>(null)
  const returnFocus = useRef(false)
  const disabled = busy || !status?.available || !isYourTurn || recovery !== 'ready'
  const extras = draftExtras ?? selectedExtras.map(kind => ({ kind, stake: Number(extraStake) }))
  const draftValid = stake.trim() !== '' && validStake(Number(stake), status) && extras.every(bet => validStake(bet.stake, status))
  const oddsValid = oddsStake.trim() !== '' && validStake(Number(oddsStake), status)
  const totals = round ? roundTotals(round) : null
  const diceRound = pendingRound ?? round
  const dice = diceRound?.lastOutcome
  const hasOdds = round?.extraBets?.some(bet => bet.kind === 'odds' && !bet.resolved)
  const canOdds = round?.phase === 'point' && !!gateway.placeOdds && !hasOdds
  const repeatExtras = round?.extraBets?.filter(bet => bet.kind !== 'odds').map(bet => ({ kind: bet.kind as CrapsExtraBetRequest['kind'], stake: bet.stake })) ?? []
  const repeatValid = !!round && validStake(round.stake, status) && repeatExtras.every(bet => validStake(bet.stake, status))
  const tableStyle = useMemo(() => tableArtworkUrl ? { '--ff-craps-table-art': 'url(' + tableArtworkUrl + ')' } as CSSProperties : undefined, [tableArtworkUrl])
  useEffect(() => { if (!busy && returnFocus.current) { if (!disabled && document.activeElement === document.body) primary.current?.focus(); returnFocus.current = false } }, [busy, disabled])
  function close() { setPanel(null); requestAnimationFrame(() => trigger.current?.focus()) }
  function open(next: Panel, button: HTMLButtonElement) { trigger.current = button; setPanel(value => value === next ? null : next) }
  useEffect(() => { if (!panel) return; closeButton.current?.focus(); const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { setPanel(null); trigger.current?.focus() } }; document.addEventListener('keydown', escape); return () => document.removeEventListener('keydown', escape) }, [panel])
  useEffect(() => { setPanel(null) }, [gateway, playerId])
  useEffect(() => { if (panel === 'odds' && !canOdds) { setPanel(null); requestAnimationFrame(() => primary.current?.focus()) } }, [panel, canOdds])
  function edit() { if (round) { setStake(String(round.stake)); const first = repeatExtras[0]; setSelectedExtras([...new Set(repeatExtras.map(bet => bet.kind))]); setDraftExtras(repeatExtras); if (first) setExtraStake(repeatExtras.every(bet => bet.stake === first.stake) ? String(first.stake) : '') }; table.clear(); primary.current?.focus() }
  const invalidDraft = !round && !!status?.available && !draftValid
  const limitCopy = status ? money(status.minimumStake) + '–' + money(status.maximumStake) + ' · steps ' + money(status.stakeIncrement) : ''
  const result = motion !== 'idle' ? 'Dice rolling…' : !isYourTurn ? activePlayerName + ' is the shooter' : !status ? 'Checking table…' : !status.available ? 'Table unavailable' : resultLabel(round)

  return <div className="ff-craps-page"><main className="ff-craps-main">
    <header className="ff-craps-title">
      {showTitle && <h1>Craps</h1>}
      <div className="ff-craps-summary" aria-label="Hand totals">
        <span>Bet <b>{money(totals?.stake ?? (draftValid ? Number(stake) + extras.reduce((sum, bet) => sum + bet.stake, 0) : 0))}</b></span>
        <span>{round?.phase === 'resolved' ? 'Total return' : 'Returned'} <b>{totals ? money(totals.returned) : '—'}</b></span>
      </div>
      <nav aria-label="Craps details">{(['bets', 'history', 'tips'] as const).map(value => <button type="button" aria-expanded={panel === value} aria-controls="ff-craps-details" onClick={event => open(value, event.currentTarget)} key={value}>{value === 'bets' ? 'Bets' : value === 'history' ? 'Rolls' : 'Tips'}</button>)}</nav>
    </header>
    <section className="ff-craps-table" aria-label="Craps table" style={tableStyle}>
      <div className="ff-craps-table__felt">
        <div className="ff-craps-point-row" aria-label={round?.phase === 'point' ? 'Point ' + round.point + ' is on' : 'Point is off'}>
          <span className="ff-craps-puck">OFF</span>
          {pointNumbers.map(number => <span className={round?.phase === 'point' && round.point === number ? 'is-point' : ''} key={number}>{round?.phase === 'point' && round.point === number && <b className="ff-craps-puck is-on">ON</b>}<strong>{number}</strong></span>)}
        </div>
        <div className="ff-craps-center">
          <div className={'ff-craps-callout ' + (round?.phase === 'resolved' ? totals!.net! > 0 ? 'is-win' : totals!.net! < 0 ? 'is-loss' : '' : '')} role="status" aria-live="polite">
            <strong>{result}</strong>
            {motion === 'idle' && round?.phase === 'resolved' && totals?.net !== null && <span>Net {totals!.net! > 0 ? '+' : totals!.net! < 0 ? '−' : ''}{money(Math.abs(totals!.net!))}</span>}
          </div>
          <div className="ff-craps-dice">
            <DiceThrow className="ff-craps-dice__stage" values={[dice?.first ?? null, dice?.second ?? null]} rollKey={diceRound ? diceRound.roundId + ':' + (diceRound.rolls.at(-1)?.rollNumber ?? 'ready') : 'ready'} rolling={motion === 'rolling'} label={motion !== 'idle' ? 'Dice rolling' : dice ? 'Latest roll: ' + dice.first + ' and ' + dice.second + ', total ' + dice.total : 'Two dice ready to roll'} />
            <span className="ff-craps-dice__total">{motion !== 'idle' ? 'Rolling' : dice ? dice.total : '—'}</span>
          </div>
          <div className="ff-craps-rolls" aria-label="Recent roll totals">{round?.rolls.slice(-4).map(item => <span key={item.rollNumber} title={'Roll ' + item.rollNumber + ': ' + item.first + ' + ' + item.second + ' = ' + item.total}><small>#{item.rollNumber}</small><b>{item.total}</b></span>)}</div>
        </div>
        <div className="ff-craps-propositions" aria-label="One-roll bets">
          {extraBetOptions.map(option => {
            const placed = round?.extraBets?.filter(bet => bet.kind === option.kind) ?? []
            const amount = placed.reduce((sum, bet) => sum + bet.stake, 0)
            const returned = placed.reduce((sum, bet) => sum + (bet.totalReturn ?? 0), 0)
            const selected = !round && selectedExtras.includes(option.kind)
            const state = placed.length ? placed.some(bet => !bet.resolved) ? 'is-working' : returned > 0 ? 'is-win' : 'is-loss' : selected ? 'is-selected' : ''
            return <button className={'ff-craps-proposition ' + state} type="button" key={option.kind} disabled={!!round || disabled} aria-pressed={!!round ? placed.some(bet => !bet.resolved) : selected} aria-label={option.label + (round ? amount ? ': ' + money(amount) + (placed.every(bet => bet.resolved) ? ', returned ' + money(returned) : ', working') : ', no bet' : ', ' + option.coverageText + ', pays ' + option.payout)} onClick={() => { setSelectedExtras(values => values.includes(option.kind) ? values.filter(kind => kind !== option.kind) : [...values, option.kind]); if (draftExtras) setDraftExtras(values => values!.some(bet => bet.kind === option.kind) ? values!.filter(bet => bet.kind !== option.kind) : [...values!, { kind: option.kind, stake: Number(extraStake) || 5 }]) }}>
              <strong>{option.label}</strong><small>{option.coverageText}</small>
              {(selected || amount > 0 && placed.some(bet => !bet.resolved)) && <b className="ff-craps-bet-marker">{chip(round ? amount : extras.filter(bet => bet.kind === option.kind).reduce((sum, bet) => sum + bet.stake, 0))}</b>}
              {placed.length > 0 && placed.every(bet => bet.resolved) && <span>{returned > 0 ? 'Returned ' + money(returned) : 'Lost'}</span>}
            </button>
          })}
        </div>
        <div className={'ff-craps-pass-line ' + (round && round.phase !== 'resolved' ? 'has-bet' : '')}>
          <strong>PASS LINE</strong><span>{round ? round.phase === 'resolved' ? 'Returned ' + money(round.lastOutcome?.totalReturn ?? 0) : money(round.stake) : 'Pays 1:1'}</span>
          {hasOdds && <b>Odds {money(round!.extraBets!.filter(bet => bet.kind === 'odds' && !bet.resolved).reduce((sum, bet) => sum + bet.stake, 0))}</b>}
        </div>
      </div>
      <div className="ff-craps-controls">
        {!round ? <>
          <label className="ff-craps-stake-label">Pass Line<span className="ff-craps-stake-input"><b>R</b><input aria-label="Pass Line bet in Rand" type="number" min={status?.minimumStake ?? 1} max={status?.maximumStake ?? 100} step={status?.stakeIncrement ?? 1} value={stake} disabled={disabled} aria-invalid={!!status && !validStake(Number(stake), status)} onChange={event => setStake(event.target.value)} /></span></label>
          <label className="ff-craps-extra-label">Each one-roll<span className="ff-craps-stake-input"><b>R</b><input aria-label="Each one-roll bet in Rand" type="number" min={status?.minimumStake ?? 1} max={status?.maximumStake ?? 100} step={status?.stakeIncrement ?? 1} value={extraStake} placeholder={draftExtras?.length ? 'Mixed' : undefined} disabled={disabled} aria-invalid={extras.some(bet => !validStake(bet.stake, status))} onChange={event => { setExtraStake(event.target.value); if (draftExtras) setDraftExtras(values => values!.map(bet => ({ ...bet, stake: Number(event.target.value) }))) }} /></span></label>
          <div className="ff-craps-chip-row" aria-label="Quick Pass Line stakes">{availableChipValues(status).map(value => <button type="button" disabled={disabled} aria-pressed={Number(stake) === value} onClick={() => setStake(String(value))} key={value}>R{value}</button>)}</div>
        </> : <div className="ff-craps-hand-actions">
          {round.phase === 'resolved' ? <><button type="button" disabled={disabled} onClick={edit}>Edit bet</button></> : <><span>Working <b>{money(totals!.workingStake)}</b></span>{canOdds && <button type="button" disabled={disabled} aria-expanded={panel === 'odds'} onClick={event => open('odds', event.currentTarget)}>Add odds</button>}</>}
        </div>}
        <button className="ff-craps-primary" ref={primary} type="button" disabled={disabled || (!round && !draftValid) || (round?.phase === 'resolved' && !repeatValid)} onClick={event => { returnFocus.current = document.activeElement === event.currentTarget || event.detail === 0; setPanel(null); if (!round) table.start(Number(stake), extras); else if (round.phase === 'resolved') table.start(round.stake, repeatExtras); else table.roll() }}>
          {busy ? motion === 'idle' ? 'Checking…' : 'Rolling…' : !isYourTurn ? 'Waiting' : !round ? 'Bet R' + (Number(stake) || 0) + ' on Pass Line' : round.phase === 'resolved' ? 'Repeat ' + chip(round.stake + repeatExtras.reduce((sum, bet) => sum + bet.stake, 0)) : round.phase === 'come-out' ? 'Roll come-out' : 'Roll point ' + round.point}
        </button>
      </div>
    </section>
    <div className="ff-craps-notice" role={error ? 'alert' : undefined}>
      <span>{error ?? (invalidDraft ? 'Enter a valid bet: ' + limitCopy : '')}</span>
      {(recovery === 'failed' || !status || status && !status.available) && !busy && <button type="button" onClick={table.retry}>Check again</button>}
    </div>
    {panel && <section className="ff-craps-details" id="ff-craps-details" role="dialog" aria-label={panel === 'tips' ? 'Craps tips' : panel === 'history' ? 'Roll history' : panel === 'odds' ? 'Pass Line odds' : 'Hand bets'}>
      <header><h2>{panel === 'tips' ? 'Craps tips' : panel === 'history' ? 'Roll history' : panel === 'odds' ? 'Pass Line odds' : 'Hand bets'}</h2><button type="button" aria-label="Close Craps details" ref={closeButton} onClick={close}>×</button></header>
      <div className="ff-craps-details__body">
        {panel === 'tips' ? <><h3>Pass Line</h3><p>On the come-out, 7 or 11 wins; 2, 3 or 12 loses. Any other total sets the point. Make that point before 7 to win. Pays 1:1.</p><h3>One-roll bets</h3>{extraBetOptions.map(option => <p key={option.kind}><b>{option.label}</b> · {option.coverageText}. Pays {option.payout}.</p>)}<h3>Pass odds</h3><p>Add once after a point is set. Pays 2:1 on 4 or 10, 3:2 on 5 or 9, and 6:5 on 6 or 8. Odds settle with the Pass Line. Repeat includes Pass Line and one-roll bets; add odds again after a point is set.</p><p>Returns include the original bet. {limitCopy}</p></> : panel === 'history' ? round?.rolls.length ? <ol>{round.rolls.map(item => <li key={item.rollNumber}><b>#{item.rollNumber}</b><span>{item.first} + {item.second} = <strong>{item.total}</strong></span><small>{rollResultLabel(item.result)}</small></li>)}</ol> : <p>No rolls in this hand.</p> : panel === 'odds' ? <form onSubmit={event => { event.preventDefault(); if (!disabled && oddsValid) table.placeOdds(Number(oddsStake)) }}><p>Point {round?.point} · {oddsCopy(round?.point ?? null)}</p><label>Odds bet <input aria-label="Pass Line odds stake" type="number" min={status?.minimumStake ?? 1} max={status?.maximumStake ?? 100} step={status?.stakeIncrement ?? 1} value={oddsStake} disabled={disabled} aria-invalid={!oddsValid} onChange={event => setOddsStake(event.target.value)} /></label><small>{limitCopy}</small><button type="submit" disabled={disabled || !oddsValid}>Place odds</button></form> : round ? <><div className="ff-craps-bet-entry"><b>Pass Line</b><span>{money(round.stake)}</span><small>{round.phase === 'resolved' ? 'Returned ' + money(round.lastOutcome?.totalReturn ?? 0) : 'Working'}</small></div>{round.extraBets?.map((bet, index) => <div className="ff-craps-bet-entry" key={index}><b>{extraBetLabel(bet.kind)}</b><span>{money(bet.stake)}</span><small>{bet.resolved ? 'Returned ' + money(bet.totalReturn ?? 0) : 'Working'}</small></div>)}<p>Total bet {money(totals!.stake)} · Returned {money(totals!.returned)}</p></> : <><p>Pass Line {money(Number(stake) || 0)}</p>{draftExtras && new Set(draftExtras.map(bet => bet.stake)).size > 1 && <p>Amounts differ. Entering a one-roll stake changes each bet.</p>}{extras.map((bet, index) => <p key={index}>{extraBetLabel(bet.kind)} {money(bet.stake || 0)}</p>)}</>}
      </div>
    </section>}
  </main></div>
}

/** Retained for package consumers; the table uses compact resultLabel copy. */
export function tableMessage(round: CrapsRound | null, serviceReady: boolean): string {
  if (!round) return serviceReady ? 'Place a Pass Line bet before the come-out roll.' : 'Checking the table…'
  const result = round.lastOutcome?.result
  if (!result) return 'Come-out roll ready. A 7 or 11 wins the Pass Line.'
  switch (result) {
    case 'natural-win': return 'Natural ' + round.lastOutcome?.total + '. Pass Line wins!'
    case 'craps-loss': return 'Craps ' + round.lastOutcome?.total + '. Pass Line loses.'
    case 'point-established': return 'Point is ' + round.point + '. Roll it again before a 7.'
    case 'point-hit': return 'Point ' + round.point + ' made. Pass Line wins!'
    case 'seven-out': return 'Seven-out. Pass Line loses and the dice pass.'
    case 'no-decision': return round.lastOutcome?.total + ', no decision. Point remains ' + round.point + '.'
  }
}

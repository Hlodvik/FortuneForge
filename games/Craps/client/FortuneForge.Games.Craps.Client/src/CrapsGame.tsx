import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import type { CrapsExtraBetRequest, CrapsGateway, CrapsRound } from './contracts'
import { DiceThrow } from './DiceThrow'
import { CrapsBetControls } from './CrapsBetControls'
import { disposeCrapsAudio, getCrapsMuted, playCrapsClick, playCrapsResult, playCrapsRoll, setCrapsMuted } from './crapsAudio'
import chip025 from './assets/chips/chip-025.png'
import chip050 from './assets/chips/chip-050.png'
import chip1 from './assets/chips/chip-1.png'
import chip2 from './assets/chips/chip-2.png'
import chip5 from './assets/chips/chip-5.png'
import chip10 from './assets/chips/chip-10.png'
import chip25 from './assets/chips/chip-25.png'
import chip50 from './assets/chips/chip-50.png'
// The table's layout and assets remain owned by the Craps package.
import { extraBetLabel, extraBetOptions, oddsCopy, pointNumbers, resultLabel, rollResultLabel, roundTotals, validStake } from './crapsPresentation'
import { useCrapsTable } from './useCrapsTable'
import './craps.css'
import './crapsEnhancements.css'
import './crapsViewport.css'
import './crapsCasino.css'

export type CrapsGameProps = Readonly<{
  gateway: CrapsGateway; playerId?: string; showTitle?: boolean; tableArtworkUrl?: string
  isYourTurn?: boolean; activePlayerName?: string; onRoundChange?: (round: CrapsRound | null) => void
}>
type Panel = 'tips' | 'history' | 'bets' | 'odds' | null
const money = (value: number) => 'R' + value.toFixed(2)
const chip = (value: number) => 'R' + Number(value.toFixed(2))
const chipPictures: Record<number, string> = { .25: chip025, .5: chip050, 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25, 50: chip50 }
// Existing PNGs have different transparent margins; match visible chip diameters.
const chipArtScale = (picture: string) => picture === chip50 ? 1.2 : picture === chip025 ? 60 / 53 : picture === chip050 ? 60 / 58 : 1

export function CrapsGame({ gateway, playerId, showTitle = true, tableArtworkUrl, isYourTurn = true, activePlayerName = 'Another player', onRoundChange }: CrapsGameProps) {
  const table = useCrapsTable(gateway, playerId, isYourTurn, onRoundChange)
  const { status, round, busy, motion, pendingRound, error, recovery } = table
  const [stake, setStake] = useState('10')
  const [extraStake, setExtraStake] = useState('5')
  const [oddsStake, setOddsStake] = useState('10')
  const [selectedExtras, setSelectedExtras] = useState<readonly CrapsExtraBetRequest['kind'][]>([])
  const [draftExtras, setDraftExtras] = useState<readonly CrapsExtraBetRequest[] | null>(null)
  const [selectedChip, setSelectedChip] = useState(5)
  const [muted, setMuted] = useState(getCrapsMuted)
  const [draftError, setDraftError] = useState<string | null>(null)
  const [draftHistory, setDraftHistory] = useState<readonly { stake: string; extraStake: string; selectedExtras: readonly CrapsExtraBetRequest['kind'][]; draftExtras: readonly CrapsExtraBetRequest[] | null }[]>([])
  const rollFeedback = useRef<{ id: string; rolls: number } | null>(null)
  const [panel, setPanel] = useState<Panel>(null)
  const trigger = useRef<HTMLButtonElement | null>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const primary = useRef<HTMLButtonElement>(null)
  const returnFocus = useRef(false)
  const disabled = busy || !status?.available || !isYourTurn || recovery !== 'ready'
  const chipOptions = useMemo(() => [.25, .5, 1, 2, 5, 10, 25, 50].filter(value => validStake(value, status))
    .map(value => ({ value, picture: chipPictures[value], artScale: chipArtScale(chipPictures[value]) })), [status])
  useEffect(() => {
    if (chipOptions.length && !chipOptions.some(item => item.value === selectedChip)) setSelectedChip(chipOptions[0].value)
  }, [chipOptions, selectedChip])
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
  useEffect(() => () => disposeCrapsAudio(), [])
  useEffect(() => {
    const previous = rollFeedback.current
    if (busy || motion !== 'idle' || !previous) return
    if (round?.roundId === previous.id && round.rolls.length > previous.rolls) {
      rollFeedback.current = null
      if (round.phase === 'resolved') playCrapsResult((totals?.net ?? 0) > 0)
    } else if (error) rollFeedback.current = null
  }, [busy, motion, round, error, totals?.net])
  function remember() { setDraftHistory(values => [...values.slice(-29), { stake, extraStake, selectedExtras, draftExtras }]); setDraftError(null) }
  function addChip(kind: 'pass' | CrapsExtraBetRequest['kind']) {
    if (disabled || round) return
    const amount = Math.round(((kind === 'pass' ? Number(stake) || 0 : extras.find(bet => bet.kind === kind)?.stake ?? 0) + selectedChip) * 100) / 100
    if (!validStake(amount, status)) { setDraftError('Bet limit: ' + (status ? money(status.minimumStake) + '–' + money(status.maximumStake) : 'Table unavailable')); return }
    remember()
    if (kind === 'pass') setStake(String(amount))
    else setDraftExtras([...extras.filter(bet => bet.kind !== kind), { kind, stake: amount }])
  }
  function undoDraft() {
    if (disabled || round || !draftHistory.length) return
    const previous = draftHistory[draftHistory.length - 1]
    setStake(previous.stake); setExtraStake(previous.extraStake); setSelectedExtras(previous.selectedExtras); setDraftExtras(previous.draftExtras); setDraftHistory(values => values.slice(0, -1)); setDraftError(null)
  }
  function clearDraft() { if (disabled || round) return; remember(); setStake('0'); setSelectedExtras([]); setDraftExtras(null) }
  function primaryAction() {
    if (disabled) return
    returnFocus.current = true; setPanel(null)
    if (!round) { if (draftValid) { setDraftError(null); table.start(Number(stake), extras) } }
    else if (round.phase === 'resolved') { if (repeatValid) table.start(round.stake, repeatExtras) }
    else { rollFeedback.current = { id: round.roundId, rolls: round.rolls.length }; playCrapsRoll(); table.roll() }
  }
  function close() { setPanel(null); requestAnimationFrame(() => trigger.current?.focus()) }
  function open(next: Panel, button: HTMLButtonElement) { trigger.current = button; setPanel(value => value === next ? null : next) }
  useEffect(() => { if (!panel) return; closeButton.current?.focus(); const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { setPanel(null); trigger.current?.focus() } }; document.addEventListener('keydown', escape); return () => document.removeEventListener('keydown', escape) }, [panel])
  useEffect(() => { setPanel(null); setDraftHistory([]); setDraftError(null); setStake('10'); setSelectedExtras([]); setDraftExtras(null); rollFeedback.current = null }, [gateway, playerId])
  useEffect(() => { if (panel === 'odds' && !canOdds) { setPanel(null); requestAnimationFrame(() => primary.current?.focus()) } }, [panel, canOdds])
  function edit() { setDraftHistory([]); setDraftError(null); if (round) { setStake(String(round.stake)); const first = repeatExtras[0]; setSelectedExtras([...new Set(repeatExtras.map(bet => bet.kind))]); setDraftExtras(repeatExtras); if (first) setExtraStake(repeatExtras.every(bet => bet.stake === first.stake) ? String(first.stake) : '') }; table.clear(); primary.current?.focus() }
  const invalidDraft = !round && !!status?.available && Number(stake) > 0 && !draftValid
  const draftTotal = Math.round((Number(stake) || 0) * 100 + extras.reduce((sum, bet) => sum + Math.round(bet.stake * 100), 0)) / 100
  const limitCopy = status ? money(status.minimumStake) + '–' + money(status.maximumStake) + ' · steps ' + money(status.stakeIncrement) : ''
  const result = motion !== 'idle' ? 'Dice rolling…' : !isYourTurn ? activePlayerName + ' is the shooter' : !status ? 'Checking table…' : !status.available ? 'Table unavailable' : resultLabel(round)

  return <div className="ff-craps-page" onClickCapture={event => {
    const button = (event.target as Element).closest?.('button')
    if (button && !button.disabled) playCrapsClick()
  }}><main className="ff-craps-main">
    <header className="ff-craps-title">
      {showTitle && <h1>Craps</h1>}
      <span className="ff-craps-mode">Free play</span>
      <span className="ff-craps-limits">{status ? money(status.minimumStake) + '–' + money(status.maximumStake) + ' per bet' : 'Connecting to table'}</span>
      <nav aria-label="Craps details">
        <button className="ff-craps-sound" type="button" aria-label={muted ? 'Unmute Craps sounds' : 'Mute Craps sounds'} aria-pressed={muted} onClick={() => { setCrapsMuted(!muted); setMuted(!muted) }}><ControlIcon kind={muted ? 'muted' : 'sound'} /></button>
        {(['bets', 'history', 'tips'] as const).map(value => <button type="button" aria-expanded={panel === value} aria-controls="ff-craps-details" onClick={event => open(value, event.currentTarget)} key={value}>{value === 'bets' ? 'Bets' : value === 'history' ? 'Rolls' : 'Tips'}</button>)}
      </nav>
    </header>
    <section className={`ff-craps-table${!tableArtworkUrl ? ' ff-craps-table--default-art' : ''}`} aria-label="Craps table" style={tableStyle}>
      <div className="ff-craps-table__felt"><div className="ff-craps-board">
        <div className="ff-craps-point-row" aria-label={round?.phase === 'point' ? 'Point ' + round.point + ' is on' : 'Point is off'}>
          <span className="ff-craps-puck">OFF</span>
          {pointNumbers.map(number => <span className={round?.phase === 'point' && round.point === number ? 'is-point' : ''} key={number}>{round?.phase === 'point' && round.point === number && <b className="ff-craps-puck is-on">ON</b>}<strong>{number === 6 ? 'SIX' : number === 9 ? 'NINE' : number}</strong></span>)}
        </div>
        <div className="ff-craps-center">
          <div className="ff-craps-watermark" aria-hidden="true"><strong>CRAPS</strong><small>FORTUNE FORGE</small></div>
          <div className={'ff-craps-callout ' + (round?.phase === 'resolved' ? (totals?.net ?? 0) > 0 ? 'is-win' : (totals?.net ?? 0) < 0 ? 'is-loss' : '' : '')} role="status" aria-live="polite">
            <small>{round?.phase === 'point' ? 'POINT ' + round.point : 'COME-OUT'}</small><strong>{result}</strong>
            {motion === 'idle' && round?.phase === 'resolved' && totals?.net != null && <span>Net {totals.net > 0 ? '+' : totals.net < 0 ? '−' : ''}{money(Math.abs(totals.net))}</span>}
          </div>
          <div className="ff-craps-dice">
            <DiceThrow className="ff-craps-dice__stage" values={[dice?.first ?? null, dice?.second ?? null]} rollKey={diceRound ? diceRound.roundId + ':' + (diceRound.rolls.at(-1)?.rollNumber ?? 'ready') : 'ready'} rolling={motion === 'rolling'} label={motion !== 'idle' ? 'Dice rolling' : dice ? 'Latest roll: ' + dice.first + ' and ' + dice.second + ', total ' + dice.total : 'Two dice ready to roll'} />
            {dice && <span className="ff-craps-dice__total">{motion !== 'idle' ? 'Rolling' : dice.total}</span>}
          </div>
          <div className="ff-craps-rolls" aria-label="Recent roll totals">{round?.rolls.slice(-5).map(item => <span key={item.rollNumber} title={'Roll ' + item.rollNumber + ': ' + item.first + ' + ' + item.second + ' = ' + item.total}><small>#{item.rollNumber}</small><b>{item.total}</b></span>)}</div>
        </div>
        <div className="ff-craps-propositions" aria-label="One-roll bets">
          {extraBetOptions.map(option => {
            const placed = round?.extraBets?.filter(bet => bet.kind === option.kind) ?? []
            const amount = round ? placed.reduce((sum, bet) => sum + bet.stake, 0) : extras.filter(bet => bet.kind === option.kind).reduce((sum, bet) => sum + bet.stake, 0)
            const returned = placed.reduce((sum, bet) => sum + (bet.totalReturn ?? 0), 0)
            const working = !round || placed.some(bet => !bet.resolved)
            const state = amount ? working ? 'is-working' : returned > 0 ? 'is-win' : 'is-loss' : ''
            return <button className={'ff-craps-proposition is-' + option.kind + ' ' + state} type="button" key={option.kind} disabled={!!round || disabled} aria-pressed={amount > 0 && working} aria-label={option.label + (round ? amount ? ': ' + money(amount) + (working ? ', working' : ', returned ' + money(returned)) : ', no bet' : ', add ' + chip(selectedChip) + ', ' + option.coverageText + ', pays ' + option.payout)} onClick={() => addChip(option.kind)}>
              <strong>{option.label.toUpperCase()}</strong>
              {option.kind === 'field' ? <span className="ff-craps-field-numbers" aria-hidden="true">{[2, 3, 4, 9, 10, 11, 12].map(number => <span key={number} className={number === 2 || number === 12 ? 'is-double' : ''}>{number}</span>)}</span> : <span className="ff-craps-proposition__dice" aria-hidden="true"><PrintedDie value={option.kind === 'any-seven' ? 3 : 1} /><PrintedDie value={option.kind === 'any-seven' ? 4 : 1} /></span>}
              <small className="ff-craps-proposition__payout">{option.kind === 'field' ? '2 & 12 PAY DOUBLE' : option.kind === 'any-seven' ? 'PAYS 4 TO 1' : '2 · 3 · 12  /  PAYS 7 TO 1'}</small>
              {amount > 0 && working && <ChipStack amount={amount} label={option.label} />}
              {placed.length > 0 && !working && <span className="ff-craps-proposition__caption">{returned > 0 ? 'Returned ' + money(returned) : 'Lost'}</span>}
            </button>
          })}
        </div>
        <button type="button" className={'ff-craps-pass-line ' + (round && round.phase !== 'resolved' || !round && Number(stake) > 0 ? 'has-bet' : '')} disabled={disabled || !!round} aria-label={'Pass Line, add ' + chip(selectedChip)} onClick={() => addChip('pass')}>
          <strong>PASS LINE</strong><span>PAYS 1 TO 1</span>
          {(round ? round.phase !== 'resolved' : Number(stake) > 0) && <ChipStack amount={round?.stake ?? Number(stake)} label="Pass Line" />}
          {hasOdds && <b className="ff-craps-pass-odds">ODDS {money(round!.extraBets!.filter(bet => bet.kind === 'odds' && !bet.resolved).reduce((sum, bet) => sum + bet.stake, 0))}</b>}
        </button>
      </div></div>
      <div className="ff-craps-controls">
        {!round ? <CrapsBetControls denominations={chipOptions} selectedChip={selectedChip} disabled={disabled}
          canUndo={draftHistory.length > 0} canClear={!!(Number(stake) || extras.length)}
          onSelectChip={value => { setSelectedChip(value); setDraftError(null) }} onUndo={undoDraft} onClear={clearDraft}/>
        : <div className="ff-craps-hand-actions">
          {round.phase === 'resolved' ? <button type="button" disabled={disabled} onClick={edit}>Edit bet</button> : <><span>Working <b>{money(totals!.workingStake)}</b></span>{canOdds && <button type="button" disabled={disabled} aria-expanded={panel === 'odds'} onClick={event => open('odds', event.currentTarget)}>Add odds</button>}</>}
        </div>}
        <button className="ff-craps-primary" ref={primary} type="button" aria-label={busy ? motion === 'idle' ? 'Checking…' : 'Rolling…' : !isYourTurn ? 'Waiting' : !round ? 'Bet ' + chip(Number(stake) || 0) + ' on Pass Line' : round.phase === 'resolved' ? 'Repeat ' + chip(round.stake + repeatExtras.reduce((sum, bet) => sum + bet.stake, 0)) : round.phase === 'come-out' ? 'Roll come-out' : 'Roll point ' + round.point} disabled={disabled || (!round && !draftValid) || (round?.phase === 'resolved' && !repeatValid)} onClick={primaryAction}>
          <ControlIcon kind={!round || round.phase === 'resolved' ? 'deal' : 'dice'} /><strong>{busy ? motion === 'idle' ? 'Checking…' : 'Rolling…' : !isYourTurn ? 'Waiting' : !round ? 'BET' : round.phase === 'resolved' ? 'REBET' : 'ROLL'}</strong><small>{!round ? money(Number.isFinite(draftTotal) ? draftTotal : 0) : round.phase === 'resolved' ? money(round.stake + repeatExtras.reduce((sum, bet) => sum + bet.stake, 0)) : round.phase === 'point' ? 'POINT ' + round.point : 'COME-OUT'}</small>
        </button>
      </div>
      <footer className="ff-craps-summary" aria-label="Hand totals">
        <span>Bet <b>{money(totals?.stake ?? (Number.isFinite(draftTotal) ? draftTotal : 0))}</b></span>
        <span>Win <b>{money(totals?.returned ?? 0)}</b></span>
      </footer>
    </section>
    <div className="ff-craps-notice" role={error || draftError ? 'alert' : undefined}>
      <span>{error ?? draftError ?? (invalidDraft ? 'Enter a valid bet: ' + limitCopy : '')}</span>
      {(recovery === 'failed' || !status || status && !status.available) && !busy && <button type="button" onClick={table.retry}>Check again</button>}
    </div>
    {panel && <section className="ff-craps-details" id="ff-craps-details" role="dialog" aria-label={panel === 'tips' ? 'Craps tips' : panel === 'history' ? 'Roll history' : panel === 'odds' ? 'Pass Line odds' : 'Hand bets'}>
      <header><h2>{panel === 'tips' ? 'Craps tips' : panel === 'history' ? 'Roll history' : panel === 'odds' ? 'Pass Line odds' : 'Hand bets'}</h2><button type="button" aria-label="Close Craps details" ref={closeButton} onClick={close}>×</button></header>
      <div className="ff-craps-details__body">
        {panel === 'tips' ? <><h3>Your chips, your table</h3><p>Choose a chip, then tap a betting area to add it. Tap again to build a stack. Undo reverses your last change; Clear removes draft bets. Place bets, then roll the dice.</p><h3>Pass Line</h3><p>On the come-out, 7 or 11 wins; 2, 3 or 12 loses. Any other total sets the point. Make that point before 7 to win. Pays 1:1.</p><h3>One-roll bets</h3>{extraBetOptions.map(option => <p key={option.kind}><b>{option.label}</b> · {option.coverageText}. Pays {option.payout}.</p>)}<h3>Pass odds</h3><p>Add once after a point is set. Pays 2:1 on 4 or 10, 3:2 on 5 or 9, and 6:5 on 6 or 8. Odds settle with the Pass Line. Repeat includes Pass Line and one-roll bets; add odds again after a point is set.</p><p>Returns include the original bet. {limitCopy}</p></> : panel === 'history' ? round?.rolls.length ? <ol>{round.rolls.map(item => <li key={item.rollNumber}><b>#{item.rollNumber}</b><span>{item.first} + {item.second} = <strong>{item.total}</strong></span><small>{rollResultLabel(item.result)}</small></li>)}</ol> : <p>No rolls in this hand.</p> : panel === 'odds' ? <form onSubmit={event => { event.preventDefault(); if (!disabled && oddsValid) table.placeOdds(Number(oddsStake)) }}><p>Point {round?.point} · {oddsCopy(round?.point ?? null)}</p><label>Odds bet <input aria-label="Pass Line odds stake" type="number" min={status?.minimumStake ?? 1} max={status?.maximumStake ?? 100} step={status?.stakeIncrement ?? 1} value={oddsStake} disabled={disabled} aria-invalid={!oddsValid} onChange={event => setOddsStake(event.target.value)} /></label><small>{limitCopy}</small><button type="submit" disabled={disabled || !oddsValid}>Place odds</button></form> : round ? <><div className="ff-craps-bet-entry"><b>Pass Line</b><span>{money(round.stake)}</span><small>{round.phase === 'resolved' ? 'Returned ' + money(round.lastOutcome?.totalReturn ?? 0) : 'Working'}</small></div>{round.extraBets?.map((bet, index) => <div className="ff-craps-bet-entry" key={index}><b>{extraBetLabel(bet.kind)}</b><span>{money(bet.stake)}</span><small>{bet.resolved ? 'Returned ' + money(bet.totalReturn ?? 0) : 'Working'}</small></div>)}<p>Total bet {money(totals!.stake)} · Returned {money(totals!.returned)}</p></> : <>          <details className="ff-craps-wager-inputs"><summary>Bet amounts</summary><div>
            <label>Pass Line<span className="ff-craps-stake-input"><b>R</b><input aria-label="Pass Line bet in Rand" type="number" min={status?.minimumStake ?? 1} max={status?.maximumStake ?? 100} step={status?.stakeIncrement ?? 1} value={stake} disabled={disabled} aria-invalid={!!status && Number(stake) !== 0 && !validStake(Number(stake), status)} onChange={event => { remember(); setStake(event.target.value) }} /></span></label>
            <label>Each one-roll<span className="ff-craps-stake-input"><b>R</b><input aria-label="Each one-roll bet in Rand" type="number" min={status?.minimumStake ?? 1} max={status?.maximumStake ?? 100} step={status?.stakeIncrement ?? 1} value={extraStake} disabled={disabled} onChange={event => { remember(); setExtraStake(event.target.value); if (draftExtras) setDraftExtras(values => values!.map(bet => ({ ...bet, stake: Number(event.target.value) }))) }} /></span></label>
          </div></details><p>Pass Line {money(Number(stake) || 0)}</p>{draftExtras && new Set(draftExtras.map(bet => bet.stake)).size > 1 && <p>Amounts differ. Entering a one-roll stake changes each bet.</p>}{extras.map((bet, index) => <p key={index}>{extraBetLabel(bet.kind)} {money(bet.stake || 0)}</p>)}</>}
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

function ChipStack({ amount, label }: { amount: number; label: string }) {
  if (!Number.isFinite(amount) || amount <= 0) return null
  const pictures: string[] = []
  let cents = Math.round(amount * 100)
  for (const denomination of [50, 25, 10, 5, 2, 1, .5, .25]) {
    while (cents >= denomination * 100 && pictures.length < 6) { pictures.push(chipPictures[denomination]); cents -= denomination * 100 }
  }
  if (!pictures.length) pictures.push(chip1)
  return <span className="ff-craps-chip-stack" aria-label={money(amount) + ' on ' + label} style={{ '--chip-stack-count': pictures.length } as CSSProperties}>
    {pictures.map((picture, index) => <img src={picture} alt="" aria-hidden="true" draggable={false} width="52" height="52" key={index} style={{ '--chip-index': index, '--chip-art-scale': chipArtScale(picture) } as CSSProperties} />)}<b>{money(amount)}</b>
  </span>
}
function PrintedDie({ value }: { value: number }) {
  const dots = value === 1 ? [4] : value === 3 ? [0, 4, 8] : [0, 2, 6, 8]
  return <span className="ff-craps-printed-die">{dots.map(dot => <i key={dot} style={{ gridArea: `${Math.floor(dot / 3) + 1} / ${dot % 3 + 1}` }} />)}</span>
}
function ControlIcon({ kind }: { kind: 'undo' | 'clear' | 'deal' | 'dice' | 'sound' | 'muted' }) {
  return <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    {kind === 'undo' ? <path d="M8 4 3 9l5 5M3 9h10a7 7 0 0 1 0 14" transform="translate(0 -2)" /> : kind === 'clear' ? <path d="m6 6 12 12M18 6 6 18" /> : kind === 'deal' ? <><path d="m9 5 10 7-10 7Z" /><path d="M4 5v14" /></> : kind === 'dice' ? <><rect x="3" y="3" width="18" height="18" rx="4" /><path d="M8 8h.01M16 8h.01M12 12h.01M8 16h.01M16 16h.01" strokeWidth="3" /></> : <><path d="m11 4-5 4H3v8h3l5 4Z" />{kind === 'muted' ? <path d="m16 8 5 8m0-8-5 8" /> : <><path d="M15 8a6 6 0 0 1 0 8M18 5a10 10 0 0 1 0 14" /></>}</>}
  </svg>
}

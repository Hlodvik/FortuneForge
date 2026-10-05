import { useEffect, useRef, useState, type CSSProperties } from 'react'
import type { RouletteBet, RouletteBetKind, RouletteGateway } from './contracts'
import { pocketColor } from './rouletteHelpers'
import { availableChipValues, betOptions, coveredPockets, formatBetLabel, legalSelection, makeBet, neighborPockets, validStake } from './roulettePresentation'
import { useRouletteTable } from './useRouletteTable'
import { RouletteWheel } from './RouletteWheel'
import { RouletteRacetrack } from './RouletteRacetrack'
import './roulette.css'
import './rouletteEnhancements.css'
import './rouletteViewport.css'

export type RouletteGameProps = Readonly<{ gateway: RouletteGateway; playerId?: string; showTitle?: boolean }>
type Panel = 'bets' | 'options' | 'history' | 'neighbors' | 'tips'
const money = (value: number) => 'R' + value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const outside: readonly RouletteBetKind[] = ['low', 'even', 'red', 'black', 'odd', 'high']

export function RouletteGame({ gateway, playerId, showTitle = true }: RouletteGameProps) {
  const table = useRouletteTable(gateway, playerId)
  const { round, status, busy, spinning, error, recovery } = table
  const [kind, setKind] = useState<RouletteBetKind>('straight')
  const [numbers, setNumbers] = useState<readonly number[]>([17])
  const [stake, setStake] = useState(1)
  const [panel, setPanel] = useState<Panel | null>(null)
  const [neighborPocket, setNeighborPocket] = useState(17)
  const [neighborDepth, setNeighborDepth] = useState(1)
  const trigger = useRef<HTMLElement | null>(null)
  const panelRef = useRef<HTMLElement>(null)
  const kindRef = useRef<HTMLSelectElement>(null)
  const wasBusy = useRef(busy)
  const previouslySettled = useRef(false)
  useEffect(() => { if (status) setStake(current => validStake(current, status, status.maximumStake) ? current : status.minimumStake) }, [status])
  useEffect(() => {
    if (!panel) return
    panelRef.current?.querySelector<HTMLButtonElement>('button')?.focus()
    const listener = (event: KeyboardEvent) => { if (event.key === 'Escape') { event.preventDefault(); closePanel() } }
    document.addEventListener('keydown', listener)
    return () => document.removeEventListener('keydown', listener)
  }, [panel])
  useEffect(() => {
    if (wasBusy.current && !busy && round && document.activeElement === document.body) kindRef.current?.focus()
    wasBusy.current = busy
  }, [busy, round])

  const blocked = busy || recovery !== 'ready' || status?.available !== true
  const locked = round?.phase === 'settled'
  const balance = round?.balance ?? status?.startingBalance ?? 0
  const bets = round?.bets ?? []
  const total = bets.reduce((sum, bet) => sum + bet.stake, 0)
  const totalReturn = round?.settlements.reduce((sum, item) => sum + item.totalReturn, 0) ?? 0
  const selection = legalSelection(kind, numbers)
  const request = selection ? makeBet(kind, numbers, stake || 1) : null
  const selectedPockets = ['straight', 'split', 'street', 'corner', 'six-line'].includes(kind) ? request ? coveredPockets(request) : numbers : []
  const label = request ? formatBetLabel(request) : betOptions.find(option => option.kind === kind)!.label + ' · ' + numbers.join(', ')
  const selectedOption = betOptions.find(option => option.kind === kind)!
  const canAdd = !blocked && !locked && !!round && selection && validStake(stake, status, balance)
  const neighbors = neighborPockets(neighborPocket, neighborDepth)
  const canBatch = (source: readonly RouletteBet[]) => source.length > 0 && !!status && source.every(bet => validStake(bet.stake, status, status.startingBalance)) && source.reduce((sum, bet) => sum + bet.stake, 0) <= status.startingBalance
  const marker = (test: (bet: RouletteBet) => boolean) => bets.filter(test).reduce((sum, bet) => sum + bet.stake, 0)
  const result = !spinning && locked ? round.winningPocket : null
  const resultLabel = spinning ? 'Spinning…' : result !== null ? result + ' · ' + pocketColor(result) : 'Place bets, then spin'
  const openPanel = (next: Panel) => { if (!panel || !panelRef.current?.contains(document.activeElement)) trigger.current = document.activeElement as HTMLElement; setPanel(current => current === next ? null : next) }
  function closePanel() { setPanel(null); window.requestAnimationFrame(() => trigger.current?.focus()) }
  function chooseKind(next: RouletteBetKind) { setKind(next); setNumbers(next === 'straight' ? [numbers[0] ?? 17] : next === 'column' || next === 'dozen' ? [1] : []) }
  function chooseNumber(value: number) {
    if (!['split', 'street', 'corner', 'six-line'].includes(kind)) { setKind('straight'); setNumbers([value]); return }
    setNumbers(current => current.includes(value) ? current.filter(number => number !== value) : [...(current.length >= selectedOption.count ? [] : current), value].sort((a, b) => a - b))
  }
  function start(repeat = false) { previouslySettled.current = !!locked; closePanel(); table.open(repeat ? bets : []); }
  useEffect(() => { if (previouslySettled.current && !busy && round?.phase === 'open') { previouslySettled.current = false; kindRef.current?.focus() } }, [busy, round?.phase])
  const selectionHint = !selection ? numbers.length !== selectedOption.count ? 'Choose ' + selectedOption.count + ' pockets · ' + numbers.length + ' selected' : 'Choose adjoining pockets for a ' + selectedOption.label.toLowerCase() : !validStake(stake, status, balance) && status ? 'Chip must be ' + money(status.minimumStake) + '–' + money(Math.min(status.maximumStake, balance)) + ' in steps of ' + money(status.stakeIncrement) : label + ' · ' + selectedOption.payout

  return <main className="ff-roulette-page ff-roulette-main" aria-label="Roulette" aria-busy={busy}>
    {showTitle && <h1 className="ff-roulette-visually-hidden">Roulette</h1>}
    <header className="ff-roulette-title">
      <div className="ff-roulette-balance"><small>Table balance</small><strong>{money(balance)}</strong><span>Bet {money(total)}</span></div>
      <div className={'ff-roulette-result' + (locked && !spinning ? ' is-settled' : '')} aria-live="polite" aria-atomic="true"><span className={'ff-roulette-pocket ' + (result === null ? 'idle' : pocketColor(result))}>{result ?? '—'}</span><div><strong>{resultLabel}</strong><span>{locked && !spinning ? money(totalReturn) + ' total return' : 'Single-zero table'}</span></div></div>
      <div className="ff-roulette-title-actions"><button onClick={() => openPanel('bets')} aria-label={locked ? 'Roulette settlement' : 'Roulette active bets'} aria-expanded={panel === 'bets'}>Bets <b>{bets.length}</b></button><button onClick={() => openPanel('options')} aria-label="Roulette options" aria-expanded={panel !== null && panel !== 'bets'}>More</button></div>
    </header>
    <section className={'ff-roulette-table' + (spinning ? ' is-spinning' : '')} aria-label="Roulette table">
      <aside className="ff-roulette-wheel" aria-hidden="true"><div className="ff-roulette-wheel-image"><RouletteWheel spinning={spinning} settledPocket={result} /></div><div className="ff-roulette-recent">{table.history.slice(0, 6).map(entry => <span className={pocketColor(entry.pocket)} key={entry.roundId}>{entry.pocket}</span>)}</div></aside>
      <div className="ff-roulette-felt">
      <button className="ff-roulette-track" type="button" aria-label="Roulette neighbours" onClick={() => openPanel('neighbors')}><RouletteRacetrack /></button>
      <div className="ff-roulette-board">
        {renderPocket(0)}
        <div className="number-grid">{Array.from({ length: 36 }, (_, index) => renderPocket(index + 1))}</div>
        <div className="column-bets">{[1, 2, 3].map(value => renderArea('column', '2:1', value))}</div>
        <div className="dozen-bets">{[1, 2, 3].map(value => renderArea('dozen', value === 1 ? '1st 12' : value === 2 ? '2nd 12' : '3rd 12', value))}</div>
        <div className="outside-grid">{outside.map(value => renderArea(value, betOptions.find(option => option.kind === value)!.label))}</div>
      </div>
      </div>
      {panel && <section className="ff-roulette-panel" ref={panelRef} role="dialog" aria-modal="false" aria-label={panel === 'bets' ? locked ? 'Roulette settlement' : 'Roulette active bets' : 'Roulette options'}>
        <header><h2>{panel === 'bets' ? locked ? 'Settlement' : 'Active bets' : panel === 'neighbors' ? 'Neighbours' : panel === 'history' ? 'Recent spins' : panel === 'tips' ? 'Table guide' : 'Table options'}</h2><button aria-label="Close Roulette panel" onClick={closePanel}>×</button></header>
        <div className="ff-roulette-panel-content">
          {panel !== 'bets' && <nav aria-label="Roulette information"><button aria-pressed={panel === 'neighbors'} onClick={() => setPanel('neighbors')}>Neighbours</button><button aria-pressed={panel === 'history'} onClick={() => setPanel('history')}>History</button><button aria-pressed={panel === 'tips'} onClick={() => setPanel('tips')}>Tips</button></nav>}
          {panel === 'options' && <p>Single-zero table · Chip {status ? money(status.minimumStake) + '–' + money(status.maximumStake) : '—'}</p>}
          {panel === 'bets' && <>{!bets.length ? <p>No chips on the table.</p> : <><div className="ff-roulette-panel-summary"><strong>{money(total)} bet</strong>{locked ? <strong>{money(totalReturn)} total return</strong> : <button disabled={blocked} onClick={table.clear}>Clear all</button>}</div><ol className="ff-roulette-bets">{bets.map((bet, index) => <li key={bet.betIndex}><span className="mini-chip">R{bet.stake}</span><div><strong>{formatBetLabel(bet)}</strong>{locked && <span>{round.settlements[index]?.won ? money(round.settlements[index].totalReturn) + ' total return' : 'No return'}</span>}</div>{!locked && <button disabled={blocked} onClick={() => table.remove(bet.betIndex)} aria-label={'Remove ' + formatBetLabel(bet) + ' chip ' + (index + 1)}>Remove</button>}</li>)}</ol></>}</>}
          {panel === 'neighbors' && <div className="ff-roulette-racetrack"><label>Centre<select value={neighborPocket} disabled={blocked || locked} onChange={event => setNeighborPocket(Number(event.target.value))}>{Array.from({length:37},(_,value) => <option key={value} value={value}>{value}</option>)}</select></label><label>Each side<select value={neighborDepth} disabled={blocked || locked} onChange={event => setNeighborDepth(Number(event.target.value))}><option value={1}>1 neighbour</option><option value={2}>2 neighbours</option></select></label><div className="ff-roulette-neighbor-preview">{neighbors.map(value => <span key={value} className={pocketColor(value)}>{value}</span>)}</div><strong>{neighbors.length} chips · {money(stake * neighbors.length)}</strong><button className="primary" disabled={blocked || locked || !round || !validStake(stake, status, balance) || stake * neighbors.length > balance} onClick={() => table.add(neighbors.map(number => makeBet('straight', [number], stake)))}>Add neighbours</button></div>}
          {panel === 'history' && <>{!table.history.length ? <p>No spins on this table yet.</p> : <><div className="ff-roulette-history" aria-label="Recent winning numbers">{table.history.map(entry => <span key={entry.roundId} className={pocketColor(entry.pocket)}>{entry.pocket}</span>)}</div><dl className="ff-roulette-stats">{(['red','black','green'] as const).map(color => <div key={color}><dt>{color === 'green' ? 'Zero' : color === 'red' ? 'Red' : 'Black'}</dt><dd>{table.history.filter(entry => pocketColor(entry.pocket) === color).length}</dd></div>)}</dl><p>Last {table.history.length} spins for this account on this device. Past results do not change the odds.</p></>}</>}
          {panel === 'tips' && <div className="ff-roulette-tips"><h3>Inside bets</h3><p>Choose a bet type, select adjoining pockets, then add a chip. Split covers two, street three, corner four, and six-line six. Numbers can overlap; each chip settles independently.</p><dl>{betOptions.map(option => <div key={option.kind}><dt>{option.label}</dt><dd>{option.payout}</dd></div>)}</dl><h3>Zero &amp; table balance</h3><p>Zero is green and loses outside bets. No 00 is offered. Each new practice table starts with {money(status?.startingBalance ?? 1000)}; its balance is separate from your account wallet.</p><p>Remove chips or clear all before spinning. Neighbours places separate straight-up chips around the European wheel.</p></div>}
        </div>
      </section>}
    </section>
    <section className="ff-roulette-controls" aria-label="Roulette controls">
      <div className="ff-roulette-selection"><label><span className="ff-roulette-visually-hidden">Bet type</span><select ref={kindRef} aria-label="Bet type" value={kind} onChange={event => chooseKind(event.target.value as RouletteBetKind)} disabled={blocked || locked}>{betOptions.map(option => <option key={option.kind} value={option.kind}>{option.label}</option>)}</select></label><button className="ff-roulette-undo rail-tool" aria-label="Undo last chip" disabled={blocked || locked || !bets.length} onClick={() => table.remove(bets.at(-1)!.betIndex)}><span aria-hidden="true">↶</span>Undo</button><button className="rail-tool" disabled={blocked || locked || !bets.length || total > balance || !bets.every(bet => validStake(bet.stake, status, balance))} onClick={() => table.add(bets)}><span aria-hidden="true">2×</span>Double</button></div>
      <div className="ff-roulette-chip-rack" role="group" aria-label="Chip denominations">{availableChipValues(status).map(value => <button type="button" key={value} aria-label={money(value) + ' chip'} aria-pressed={stake === value} disabled={blocked || locked} className={'denomination chip-' + (value <= 1 ? 'yellow' : value <= 5 ? 'red' : value <= 10 ? 'blue' : value <= 25 ? 'green' : 'black')} onClick={() => setStake(value)}><span>{value}</span></button>)}</div>
      <div className="ff-roulette-actions">{!round ? <><button className="primary" disabled={blocked} onClick={() => start()}>Open table</button><button className="primary spin" disabled><span className="ff-roulette-spin-symbol" aria-hidden="true">⟳</span><span>Spin</span></button></> : locked ? <><button className="primary" disabled={blocked} onClick={() => start()}>New round</button><button className="primary spin" disabled={blocked || !canBatch(bets)} onClick={() => start(true)}><span className="ff-roulette-spin-symbol" aria-hidden="true">⟳</span><span>Repeat bets</span></button></> : <><button className="primary" disabled={!canAdd} onClick={() => table.add([makeBet(kind, numbers, stake)])}>Add chip</button><button className="primary spin" disabled={blocked || !bets.length} onClick={() => { closePanel(); table.spin() }}><span className="ff-roulette-spin-symbol" aria-hidden="true">⟳</span><span>{spinning ? 'Spinning…' : 'Spin'}</span></button></>}</div>
      <div className={'ff-roulette-notice' + (error ? ' error' : '')} role={error ? 'alert' : 'status'} aria-live="polite"><span>{error ?? (busy && !spinning ? 'Opening table…' : spinning ? 'Bets closed' : selectionHint)}</span>{(error && (!status || recovery === 'failed' || !status.available)) && <button disabled={busy} onClick={table.retry}>{recovery === 'failed' ? 'Retry restoration' : 'Retry'}</button>}</div>
    </section>
  </main>

  function renderPocket(value: number) {
    const amount = marker(bet => bet.kind === 'straight' ? bet.number === value : ['split','street','corner','six-line'].includes(bet.kind) && bet.numbers.includes(value))
    const winning = result === value
    const preview = !locked && !spinning && selectedPockets.includes(value)
    return <button key={value} type="button" className={'number ' + pocketColor(value) + (value === 0 ? ' zero' : '') + (preview ? ' selected' : '') + (winning ? ' winning' : '') + (amount > 0 ? ' has-chip' : '')} style={value ? { '--street': Math.ceil(value/3), '--column': (value - 1) % 3 + 1 } as CSSProperties : undefined} disabled={blocked || locked} onClick={() => chooseNumber(value)} aria-pressed={preview} aria-label={'Pocket ' + value + ', ' + pocketColor(value) + (amount ? ', ' + money(amount) + ' covering chips' : '') + (winning ? ', winning pocket' : '')} data-pocket={value}><span>{value}</span>{amount > 0 && <small className="felt-chip" aria-hidden="true">{amount}</small>}</button>
  }
  function renderArea(areaKind: RouletteBetKind, text: string, value?: number) {
    const amount = marker(bet => bet.kind === areaKind && (value === undefined || bet.number === value))
    const selected = !locked && !spinning && kind === areaKind && (value === undefined || numbers[0] === value)
    return <button key={areaKind + (value ?? '')} type="button" className={areaKind + (selected ? ' selected' : '') + (locked && result !== null && coveredPockets(makeBet(areaKind, value === undefined ? [] : [value], 1)).includes(result) ? ' won-area' : '')} style={value ? { '--area': value } as CSSProperties : undefined} aria-label={areaKind === 'column' ? 'Column ' + value + ', 2 to 1' : text} aria-pressed={selected} disabled={blocked || locked} onClick={() => { setKind(areaKind); setNumbers(value === undefined ? [] : [value]) }}>{text}{amount > 0 && <small className="felt-chip" aria-hidden="true">{amount}</small>}</button>
  }
}

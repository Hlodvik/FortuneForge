import { useCallback, useEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { RouletteGatewayError, type RouletteBetKind, type RouletteGateway, type RouletteRound, type RouletteStatus } from './contracts'
import { betLabel, pocketColor } from './rouletteHelpers'
import rouletteSpinWheel from './assets/roulette-spin-wheel-v2.png?no-inline'
import './roulette.css'

export type RouletteGameProps = Readonly<{ gateway: RouletteGateway; backHref?: string; playerName?: string; tableLabel?: string }>

const betOptions: readonly { kind: RouletteBetKind; label: string; count: number }[] = [
  { kind: 'straight', label: 'Straight-up', count: 1 }, { kind: 'split', label: 'Split', count: 2 },
  { kind: 'street', label: 'Street', count: 3 }, { kind: 'corner', label: 'Corner', count: 4 },
  { kind: 'six-line', label: 'Six-line', count: 6 }, { kind: 'column', label: 'Column', count: 1 },
  { kind: 'dozen', label: 'Dozen', count: 1 }, { kind: 'red', label: 'Red', count: 0 },
  { kind: 'black', label: 'Black', count: 0 }, { kind: 'even', label: 'Even', count: 0 },
  { kind: 'odd', label: 'Odd', count: 0 }, { kind: 'low', label: '1–18', count: 0 },
  { kind: 'high', label: '19–36', count: 0 },
]
const minimumSpinMilliseconds = 1100
const europeanWheelOrder = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26] as const

export function RouletteGame({ gateway, backHref = '/games', playerName = 'Player', tableLabel = 'Free-play table' }: RouletteGameProps) {
  const [status, setStatus] = useState<RouletteStatus | null>(null)
  const [round, setRound] = useState<RouletteRound | null>(null)
  const [kind, setKind] = useState<RouletteBetKind>('straight')
  const [numbers, setNumbers] = useState<number[]>([17])
  const [stake, setStake] = useState(10)
  const [busy, setBusy] = useState(false)
  const [spinning, setSpinning] = useState(false)
  const [spinTarget, setSpinTarget] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tips, setTips] = useState(false)
  const closeRef = useRef<HTMLButtonElement>(null)
  const tipsRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(value => {
      setStatus(value)
      setStake(Math.max(1, value.minimumStake))
    }).catch((reason: unknown) => {
      if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason))
    })
    return () => controller.abort()
  }, [gateway])

  const act = useCallback(async (operation: () => Promise<RouletteRound>) => {
    setBusy(true)
    setError(null)
    try { setRound(await operation()) } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }, [])
  const openRound = () => void act(() => gateway.openRound())
  const addBet = () => {
    if (!round) return
    void act(() => gateway.placeBet(round.roundId, {
      kind,
      number: kind === 'straight' || kind === 'column' || kind === 'dozen' ? numbers[0] ?? 1 : null,
      numbers: kind === 'straight' || kind === 'column' || kind === 'dozen' ? [] : numbers,
      stake,
    }))
  }
  const removeBet = (index: number) => { if (round) void act(() => gateway.removeBet(round.roundId, index)) }
  const clearBets = () => { if (round) void act(() => gateway.clearBets(round.roundId)) }
  const spin = () => {
    if (!round || busy) return
    void (async () => {
      setBusy(true)
      setSpinning(true)
      setError(null)
      const startedAt = performance.now()
      try {
        const nextRound = await gateway.spin(round.roundId)
        setSpinTarget(nextRound.winningPocket)
        const remaining = minimumSpinMilliseconds - (performance.now() - startedAt)
        if (remaining > 0) await new Promise<void>(resolve => window.setTimeout(resolve, remaining))
        setRound(nextRound)
      } catch (reason) {
        setError(messageForError(reason))
        setSpinTarget(null)
      } finally {
        setSpinning(false)
        setBusy(false)
      }
    })()
  }
  const newRound = () => { setRound(null); setError(null); setSpinTarget(null) }
  const chooseKind = (nextKind: RouletteBetKind) => {
    setKind(nextKind)
    const option = betOptions.find(item => item.kind === nextKind)!
    setNumbers(option.count > 1 ? [] : option.count === 1 ? [nextKind === 'straight' ? 17 : 1] : [])
  }
  const chooseNumber = (value: number) => {
    if (kind === 'straight') setNumbers([value])
    else if (kind === 'column' || kind === 'dozen') setNumbers([Math.max(1, Math.min(3, value))])
    else setNumbers(toggleNumber(numbers, value))
  }
  const closeTips = () => { setTips(false); window.requestAnimationFrame(() => tipsRef.current?.focus()) }
  useEffect(() => {
    if (!tips) return
    closeRef.current?.focus()
    const handler = (event: KeyboardEvent) => { if (event.key === 'Escape') closeTips() }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [tips])

  const result = round?.winningPocket
  const ballPocket = spinning ? spinTarget ?? 0 : result
  const ballStyle = { '--roulette-ball-angle': `${wheelAngle(ballPocket ?? 0)}deg` } as CSSProperties
  const ready = status?.available === true
  const selectedLabel = betLabel(kind, numbers[0] ?? null)
  const locked = round?.phase === 'settled'

  return (
    <div className="ff-roulette-page">
      <header className="ff-roulette-header">
        <nav aria-label="Game navigation">
          <a className="ff-roulette-brand" href={backHref} aria-label="Fortune Forge home"><span aria-hidden="true">✦</span><strong>Fortune Forge</strong></a>
          <a className="ff-roulette-games" href={backHref}>Other games</a>
        </nav>
        <div className="ff-roulette-account"><strong>{playerName}</strong><span>{tableLabel}</span></div>
      </header>
      {tips && <Tips closeRef={closeRef} onClose={closeTips} />}
      <main className="ff-roulette-main">
        <section className="ff-roulette-title">
          <div><small>Single-zero table · Balance R{(round?.balance ?? status?.startingBalance ?? 0).toFixed(2)}</small><h1>Roulette</h1></div>
          <button ref={tipsRef} className="ff-roulette-tips" onClick={() => setTips(true)} aria-expanded={tips}>Tips</button>
        </section>
        <section className="ff-roulette-table" aria-label="Roulette table">
          <div className="ff-roulette-wheel" aria-live="polite">
            <img className={`ff-roulette-wheel-art${spinning ? ' spinning' : ''}`} src={rouletteSpinWheel} alt="" aria-hidden="true" />
            {(spinning || result !== null && result !== undefined) && <div className={`ff-roulette-ball-track${spinning ? ' spinning' : ' settled'}`} style={ballStyle} aria-hidden="true"><span className="ff-roulette-ball" /></div>}
            <div className={`ff-roulette-pocket ${result === null || result === undefined ? 'idle' : pocketColor(result)}`}>{result ?? '—'}</div>
            <span>WINNING POCKET</span>
            <strong>{result === null || result === undefined ? 'Place bets, then spin' : `${result} · ${pocketColor(result)}`}</strong>
          </div>
          <div className="ff-roulette-board">
            <button className={`number zero ${kind === 'straight' && numbers[0] === 0 ? 'selected' : ''}`} disabled={busy || locked} onClick={() => { setKind('straight'); setNumbers([0]) }}>0</button>
            <div className="number-grid">{Array.from({ length: 36 }, (_, index) => index + 1).map(value => <button key={value} className={`number ${pocketColor(value)} ${kind === 'straight' && numbers.includes(value) ? 'selected' : ''}`} disabled={busy || locked} onClick={() => chooseNumber(value)}>{value}</button>)}</div>
            <div className="outside-grid">{betOptions.filter(item => item.count === 0).map(item => <button key={item.kind} className={kind === item.kind ? 'selected' : ''} disabled={busy || locked} onClick={() => chooseKind(item.kind)}>{item.label}</button>)}</div>
          </div>
          <div className="ff-roulette-controls">
            <label>Bet type<select value={kind} onChange={event => chooseKind(event.target.value as RouletteBetKind)} disabled={busy || locked}>{betOptions.map(item => <option key={item.kind} value={item.kind}>{item.label}</option>)}</select></label>
            <label>Chip<select value={stake} onChange={event => setStake(Number(event.target.value))} disabled={busy || locked}>{[1, 5, 10, 25, 50].map(value => <option key={value} value={value}>R{value}</option>)}</select></label>
            {(kind === 'column' || kind === 'dozen') && <div className="scalar-options" aria-label={`${kind} selection`}>{[1, 2, 3].map(value => <button type="button" className={numbers[0] === value ? 'selected' : ''} disabled={locked} onClick={() => setNumbers([value])} key={value}>{kind === 'column' ? `Column ${value}` : `Dozen ${value}`}</button>)}</div>}
            <div className="bet-summary"><small>New chip</small><strong>{selectedLabel}</strong><span>R{stake.toFixed(2)} · {payoutFor(kind)}</span></div>
            {!round ? <button className="primary" disabled={busy || !ready} onClick={openRound}>Open table</button> : locked ? <button className="primary" onClick={newRound}>New round</button> : <><button className="primary" disabled={busy || stake > round.balance || !validSelection(kind, numbers)} onClick={addBet}>Add chip</button><button className="primary spin" disabled={busy || round.bets.length === 0} onClick={spin}>{spinning ? 'Spinning…' : 'Spin the wheel'}</button></>}
          </div>
          <div className="ff-roulette-bets" aria-label="Active bets">
            {round?.phase === 'open' && round.bets.length > 0 && <>
              <div className="bets-heading"><strong>Active chips ({round.bets.length})</strong><button type="button" onClick={clearBets} disabled={busy}>Clear all</button></div>
              {round.bets.map(bet => <div className="active-bet" key={bet.betIndex}><span className={`mini-chip ${pocketColor(bet.numbers[0] ?? 1)}`}>R{bet.stake}</span><strong>{betLabel(bet.kind, bet.number)}{bet.numbers.length > 1 ? ` · ${bet.numbers.join(', ')}` : ''}</strong><button type="button" onClick={() => removeBet(bet.betIndex)} disabled={busy} aria-label={`Remove ${betLabel(bet.kind, bet.number)} bet`}>Remove</button></div>)}
            </>}
          </div>
        </section>
        {round?.phase === 'settled' && <div className="settlement-list" aria-live="polite"><strong>Settlement</strong>{round.settlements.map((settlement, index) => { const bet = round.bets[index]; return <span key={index}>{betLabel(settlement.kind, bet?.number ?? null)}: {settlement.won ? `won R${settlement.totalReturn.toFixed(2)} total return` : 'lost'}</span> })}</div>}
        {error && <p className="ff-roulette-error" role="alert">{error}</p>}
      </main>
    </div>
  )
}

function toggleNumber(values: number[], value: number) { return values.includes(value) ? values.filter(item => item !== value) : [...values, value].sort((a, b) => a - b) }
function wheelAngle(pocket: number) { return -90 + ((europeanWheelOrder.indexOf(pocket as typeof europeanWheelOrder[number]) * 360) / europeanWheelOrder.length) }
function validSelection(kind: RouletteBetKind, values: number[]) { const count = betOptions.find(item => item.kind === kind)?.count ?? 0; return count === 0 || values.length === count }
function payoutFor(kind: RouletteBetKind) { return ({ straight: '35:1', split: '17:1', street: '11:1', corner: '8:1', 'six-line': '5:1', column: '2:1', dozen: '2:1', red: '1:1', black: '1:1', even: '1:1', odd: '1:1', low: '1:1', high: '1:1' } as const)[kind] }
function messageForError(reason: unknown) { return reason instanceof RouletteGatewayError ? reason.message : 'The Roulette table is unavailable.' }

function Tips({ closeRef, onClose }: { closeRef: RefObject<HTMLButtonElement | null>; onClose: () => void }) {
  return <div className="ff-roulette-overlay"><section className="ff-roulette-tips-dialog" role="dialog" aria-modal="true" aria-labelledby="roulette-tips-title"><header><div><small>Table guide</small><h2 id="roulette-tips-title">Roulette tips</h2></div><button ref={closeRef} onClick={onClose} aria-label="Close Roulette tips">×</button></header><div className="tips-grid"><article><h3>Inside bets</h3><p>Straight-up pays 35:1, split 17:1, street 11:1, corner 8:1, and six-line 5:1. Select the bet type, choose its pockets, then add each chip.</p></article><article><h3>Outside bets</h3><p>Columns and dozens pay 2:1. Red, Black, Even, Odd, 1–18, and 19–36 pay 1:1. Bets can overlap and every chip settles independently.</p></article><article><h3>Zero &amp; controls</h3><p>Zero is green and loses outside bets. This is a single-zero table, so American 00 is not offered. Remove individual chips or clear all before spinning.</p></article></div></section></div>
}

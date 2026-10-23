import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { DragonTigerGatewayError, dragonTigerSides, type DragonTigerCard, type DragonTigerGateway, type DragonTigerRound, type DragonTigerSide, type DragonTigerStakes, type DragonTigerStatus } from './contracts'
import { HttpDragonTigerGateway } from './httpDragonTigerGateway'
import { playDragonTigerAudio } from './dragonTigerAudio'
import chip050 from './assets/chip-050.png'
import chip1 from './assets/chip-1.png'
import chip2 from './assets/chip-2.png'
import chip5 from './assets/chip-5.png'
import chip10 from './assets/chip-10.png'
import chip25 from './assets/chip-25.png'
import chip50 from './assets/chip-50.png'
import './dragonTiger.css'

export type DragonTigerGameProps = Readonly<{ gateway?: DragonTigerGateway; playerId?: string; currencySymbol?: string; onBalanceChange?: (balance: number) => void }>
const defaultGateway = new HttpDragonTigerGateway()
const chips = [{ value: .5, image: chip050 }, { value: 1, image: chip1 }, { value: 2, image: chip2 }, { value: 5, image: chip5 }, { value: 10, image: chip10 }, { value: 25, image: chip25 }, { value: 50, image: chip50 }]
type Placement = { side: DragonTigerSide; value: number }
type Pending = { key: string; stakes: DragonTigerStakes; placements: Placement[] }
type HistoryItem = { id: string; outcome: DragonTigerSide }
const emptyStakes = (): DragonTigerStakes => ({ dragonStake: 0, tigerStake: 0, tieStake: 0 })
const stakesFor = (placements: readonly Placement[]) => placements.reduce((stakes, p) => ({ ...stakes, [`${p.side}Stake`]: Math.round((stakes[`${p.side}Stake`] + p.value) * 100) / 100 }), emptyStakes())
const sum = (s: DragonTigerStakes) => Math.round((s.dragonStake + s.tigerStake + s.tieStake) * 100) / 100
const title = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)
const money = (v: number, symbol: string) => `${v < 0 ? '−' : ''}${symbol}${Math.abs(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const isAbort = (e: unknown) => e instanceof DOMException && e.name === 'AbortError'
const rejected = (e: unknown) => e instanceof DragonTigerGatewayError && (e.status === 400 && e.code === 'dragon-tiger-invalid-request' || e.code === 'insufficient-slot-credits')
const message = (e: unknown) => e instanceof DragonTigerGatewayError ? e.message : 'The connection was interrupted. Restore the deal to confirm its result.'

export function DragonTigerGame({ gateway = defaultGateway, playerId = 'standalone-preview', currencySymbol = 'R', onBalanceChange }: DragonTigerGameProps) {
  const [status, setStatus] = useState<DragonTigerStatus | null>(null)
  const [round, setRound] = useState<DragonTigerRound | null>(null)
  const [placements, setPlacements] = useState<Placement[]>([])
  const [selectedChip, setSelectedChip] = useState(1)
  const [phase, setPhase] = useState<'loading' | 'betting' | 'dealing' | 'result' | 'recover'>('loading')
  const [stage, setStage] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState('Choose a chip, then place your bets')
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [history, setHistory] = useState<HistoryItem[]>([])
  const [muted, setMuted] = useState(() => readStorage('fortuneforge:dragon-tiger:muted') === 'true')
  const [rulesOpen, setRulesOpen] = useState(false)
  const pending = useRef<Pending | null>(null)
  const locked = useRef(false)
  const mounted = useRef(true)
  const requestScope = useRef(0)
  const onBalance = useRef(onBalanceChange); onBalance.current = onBalanceChange
  const soundMuted = useRef(muted); soundMuted.current = muted
  const storageKey = `fortuneforge:dragon-tiger:pending:${playerId}`
  const historyKey = `fortuneforge:dragon-tiger:history:${playerId}`
  const stakes = stakesFor(placements), total = sum(stakes)
  const balance = status?.balance ?? 0
  const editable = (phase === 'betting' || phase === 'result') && status?.available === true

  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => {
    requestScope.current++
    const abort = new AbortController(); locked.current = true
    setPhase('loading'); setStatus(null); setRound(null); setPlacements([]); setError(null)
    pending.current = readPending(storageKey)
    setHistory(readHistory(historyKey))
    void (async () => {
      try {
        const nextStatus = await gateway.getStatus(abort.signal)
        if (abort.signal.aborted) return
        setStatus(nextStatus)
        if (pending.current) {
          setPlacements(pending.current.placements)
          setNotice('Restoring your previous deal…')
          const restored = await gateway.createRound(pending.current.stakes, { idempotencyKey: pending.current.key, signal: abort.signal })
          if (abort.signal.aborted) return
          setRound(restored); setStage(0); setPhase('dealing')
        } else {
          const latest = await gateway.getLatestRound?.(abort.signal)
          if (abort.signal.aborted) return
          if (latest) { setStatus({ ...nextStatus, balance: latest.balance }); setRound(latest); setPlacements(reconstruct(latest)); setStage(3); recordHistory(latest); setNotice('Your last result · place chips for a new round') }
          else setNotice('Choose a chip, then place your bets')
          setPhase(latest ? 'result' : 'betting'); locked.current = false
        }
      } catch (e) {
        if (abort.signal.aborted || isAbort(e)) return
        if (pending.current && !rejected(e)) { setPhase('recover'); setError(message(e)); setNotice('Pending deal · restore before playing again') }
        else {
          pending.current = null; removeStorage(storageKey)
          setPhase('betting'); locked.current = false
          setError(e instanceof DragonTigerGatewayError ? e.message : 'Could not connect to the table. Reconnect to load your balance and table limits.')
          setNotice('Connect to the table before placing a wager')
        }
      }
    })()
    return () => abort.abort()
  }, [gateway, playerId, loadAttempt])

  function recordHistory(next: DragonTigerRound) {
    setHistory(current => {
      if (current.some(item => item.id === next.roundId)) return current
      const items = [...current, { id: next.roundId, outcome: next.outcome }].slice(-36)
      writeStorage(historyKey, JSON.stringify(items)); return items
    })
  }
  useEffect(() => {
    if (phase !== 'dealing' || !round) return
    setStage(0)
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    const reveal = (next: number) => { setStage(next); playDragonTigerAudio(next === 3 ? 'result' : 'deal', soundMuted.current) }
    const timers = [window.setTimeout(() => reveal(1), reduced ? 0 : 300), window.setTimeout(() => reveal(2), reduced ? 0 : 1050), window.setTimeout(() => {
      reveal(3); pending.current = null; removeStorage(storageKey); locked.current = false
      setStatus(current => current ? { ...current, balance: round.balance } : current)
      onBalance.current?.(round.balance); recordHistory(round); setPhase('result')
      setNotice(round.outcome === 'tie' ? 'Tie · half of each Dragon / Tiger bet returned' : `${title(round.outcome)} wins · ${round.dragonValue} ${round.outcome === 'dragon' ? '>' : '<'} ${round.tigerValue}`)
    }, reduced ? 0 : 1700)]
    return () => timers.forEach(window.clearTimeout)
  }, [round?.roundId, phase === 'dealing'])

  function place(side: DragonTigerSide) {
    if (!editable || locked.current || !status) return
    const base = phase === 'result' ? [] : placements
    const next = [...base, { side, value: selectedChip }]
    const nextStakes = stakesFor(next)
    if (sum(nextStakes) > balance) { setError('Your balance is too low for this chip.'); return }
    if (sum(nextStakes) > status.maximumTotalStake || nextStakes[`${side}Stake`] > status.maximumStake) { setError(`Table limit: ${money(status.maximumTotalStake, currencySymbol)} per round.`); return }
    if (phase === 'result') { setRound(null); setStage(0); setPhase('betting') }
    setPlacements(next); setError(null); setNotice('Place more chips or deal when you’re ready')
    playDragonTigerAudio('chip', muted)
  }
  async function submit(restoring = false) {
    if (locked.current && !restoring || !status?.available) return
    const request = restoring ? pending.current : { key: `dragon-tiger-${crypto.randomUUID()}`, stakes, placements }
    if (!request || sum(request.stakes) <= 0) return
    locked.current = true; pending.current = request; writeStorage(storageKey, JSON.stringify(request))
    const scope = requestScope.current
    setError(null); setRound(null); setStage(0); setPhase('dealing'); setNotice(restoring ? 'Restoring your deal…' : 'Bets accepted · dealing the cards…')
    try {
      const next = await gateway.createRound(request.stakes, { idempotencyKey: request.key })
      if (!mounted.current || requestScope.current !== scope) return
      setRound(next)
    } catch (e) {
      if (!mounted.current || requestScope.current !== scope) return
      if (rejected(e)) {
        pending.current = null; removeStorage(storageKey); locked.current = false; setPhase('betting')
        void gateway.getStatus().then(next => { if (mounted.current && requestScope.current === scope) setStatus(next) }).catch(() => undefined)
      } else { setPhase('recover'); setNotice('Pending deal · restore before playing again') }
      setError(message(e))
    }
  }
  function clear() { if (!editable) return; setPlacements([]); setRound(null); setStage(0); setPhase('betting'); setError(null); setNotice('Choose a chip, then place your bets') }
  function rebet() {
    if (!editable || !round) return
    const restored = placements.length ? placements : reconstruct(round)
    if (sum(stakesFor(restored)) > balance) { setError('Your balance is too low to repeat this bet.'); return }
    setPlacements(restored); setRound(null); setStage(0); setPhase('betting'); setError(null); setNotice('Previous bet repeated · deal when you’re ready'); playDragonTigerAudio('chip', muted)
  }
  const settled = stage === 3 && round !== null
  const win = settled ? Math.max(0, round.profit) : 0
  const recent = history.slice(-24)
  return <section className="ff-dt" aria-label="Dragon Tiger game">
    <header className="ff-dt__header"><div><span className="ff-dt__eyebrow">FORTUNEFORGE · TABLE GAMES</span><h1>Dragon <span>&</span> Tiger</h1></div><div className="ff-dt__tools"><button type="button" aria-label={muted ? 'Turn sound on' : 'Mute sound'} aria-pressed={!muted} onClick={() => { setMuted(!muted); writeStorage('fortuneforge:dragon-tiger:muted', String(!muted)) }}>{muted ? '♪ Off' : '♪ On'}</button><button type="button" onClick={() => setRulesOpen(true)}>How to play <span aria-hidden="true">ⓘ</span></button></div></header>
    <div className="ff-dt__table">
      <div className="ff-dt__table-top"><span><i className={'ff-dt__status-dot' + (status?.available ? ' is-on' : '')}/>{status ? status.mode.startsWith('practice') ? 'PRACTICE TABLE' : 'SOLO TABLE' : 'CONNECTING'}</span><span>{status ? `${money(status.minimumStake, currencySymbol)} min · ${money(status.maximumTotalStake, currencySymbol)} max / round` : 'Loading table limits…'}</span></div>
      <div className="ff-dt__arena"><Hand side="dragon" card={round?.dragonCard} revealed={stage >= 1} winner={settled && round.outcome === 'dragon'}/><div className="ff-dt__versus"><span>龍</span><i>VS</i><span>虎</span><small>HIGHER CARD WINS</small></div><Hand side="tiger" card={round?.tigerCard} revealed={stage >= 2} winner={settled && round.outcome === 'tiger'}/></div>
      <div className={'ff-dt__announcement' + (settled ? ` is-${round.outcome}` : '')} role="status" aria-live="polite"><strong>{settled ? round.outcome === 'tie' ? 'TIE' : `${title(round.outcome).toUpperCase()} WINS` : phase === 'dealing' ? 'THE CARDS DECIDE' : 'PLACE YOUR BETS'}</strong><span>{notice}</span></div>
      <div className="ff-dt__bets">{(['dragon', 'tie', 'tiger'] as const).map(side => <button type="button" key={side} className={`ff-dt__bet ff-dt__bet--${side}${settled && round.outcome === side ? ' is-winner' : ''}${stakes[`${side}Stake`] ? ' has-bet' : ''}`} aria-label={`Bet on ${title(side)}, pays ${side === 'tie' ? '8' : '1'} to 1, current bet ${money(stakes[`${side}Stake`], currencySymbol)}`} disabled={!editable} onClick={() => place(side)}><span className="ff-dt__bet-label">{side === 'tie' ? '和' : side === 'dragon' ? '龍' : '虎'}<strong>{title(side)}</strong><small>PAYS {side === 'tie' ? '8' : '1'}:1</small></span><span className="ff-dt__bet-stack"><ChipStack placements={placements.filter(p => p.side === side)}/><b>{money(stakes[`${side}Stake`], currencySymbol)}</b></span></button>)}</div>
      <div className="ff-dt__tie-note">Equal ranks are a tie. Dragon / Tiger bets lose half; Tie pays 8:1.</div>
      <div className="ff-dt__betting-controls"><div className="ff-dt__chip-tray" role="group" aria-label="Choose chip value">{chips.map(chip => <button type="button" key={chip.value} className={'ff-dt__chip' + (selectedChip === chip.value ? ' is-selected' : '')} aria-label={`${currencySymbol}${chip.value.toFixed(2)} chip`} aria-pressed={selectedChip === chip.value} disabled={!editable} onClick={() => { setSelectedChip(chip.value); playDragonTigerAudio('chip', muted) }}><img src={chip.image} alt="" draggable={false}/><span>{chip.value < 1 ? '.50' : chip.value}</span></button>)}</div><div className="ff-dt__actions"><button type="button" className="ff-dt__utility" disabled={!editable || phase === 'result' || !placements.length} onClick={() => { if (phase === 'result') clear(); else { setPlacements(current => current.slice(0, -1)); setError(null) } }}>↶ <span>Undo</span></button><button type="button" className="ff-dt__utility" disabled={!editable || !placements.length && !round} onClick={clear}>× <span>Clear</span></button>{round && phase !== 'dealing' && <button type="button" className="ff-dt__utility" disabled={!editable} onClick={rebet}>↻ <span>Rebet</span></button>}<button type="button" className="ff-dt__deal" disabled={!status?.available || phase === 'loading' || phase === 'dealing' || phase === 'result' || phase === 'betting' && total === 0} onClick={() => { void submit(phase === 'recover') }}>{phase === 'recover' ? 'Restore deal' : phase === 'dealing' ? 'Dealing…' : 'Deal cards'} <span aria-hidden="true">→</span></button></div></div>
      <div className="ff-dt__feedback" aria-live="polite">{error && <span role="alert">{error} {!status && <button type="button" onClick={() => setLoadAttempt(n => n + 1)}>Reconnect</button>}</span>}{status && !status.available && <span role="alert">This table is temporarily unavailable.</span>}</div>
      <footer className="ff-dt__wallet"><div><span>BET</span><strong data-testid="dragon-tiger-total-bet">{money(total, currencySymbol)}</strong></div><div><span>BALANCE</span><strong data-testid="dragon-tiger-balance">{money(balance, currencySymbol)}</strong></div><div className={win > 0 ? 'has-win' : ''}><span>WIN</span><strong data-testid="dragon-tiger-win">{money(win, currencySymbol)}</strong></div>{settled && <div className="ff-dt__return"><span>RETURNED</span><strong>{money(round.totalReturn, currencySymbol)}</strong><small>Net {round.profit > 0 ? '+' : ''}{money(round.profit, currencySymbol)}</small></div>}</footer>
    </div>
    <aside className="ff-dt__history" aria-label="Recent results"><div><strong>Recent results</strong><span>Last {history.length} hands · history does not predict the next card</span></div><div className="ff-dt__beads">{recent.length ? recent.map(item => <span key={item.id} className={`ff-dt__bead is-${item.outcome}`} title={title(item.outcome)}>{item.outcome === 'dragon' ? 'D' : item.outcome === 'tiger' ? 'T' : '='}</span>) : <span className="ff-dt__no-history">Your results will appear here</span>}</div><small>{round ? `${round.shoeCardsRemaining} / 416 cards in shoe` : '8 standard decks · Ace low · King high'}</small></aside>
    {rulesOpen && <Rules currencySymbol={currencySymbol} status={status} onClose={() => setRulesOpen(false)}/>} 
  </section>
}

const rankLabels: Record<DragonTigerCard['rank'], string> = { ace:'A', two:'2',three:'3',four:'4',five:'5',six:'6',seven:'7',eight:'8',nine:'9',ten:'10',jack:'J',queen:'Q',king:'K' }
const suitLabels = { clubs:'♣', diamonds:'♦', hearts:'♥', spades:'♠' }
function Hand({ side, card, revealed, winner }: { side: 'dragon' | 'tiger'; card?: DragonTigerCard; revealed: boolean; winner: boolean }) {
  const red = card?.suit === 'hearts' || card?.suit === 'diamonds'
  return <section className={`ff-dt__hand ff-dt__hand--${side}${winner ? ' is-winner' : ''}`} aria-label={`${title(side)} hand`}><h2>{title(side)}</h2><div className={'ff-dt__card-wrap' + (revealed ? ' is-revealed' : '')}>{revealed && card ? <div className={'ff-dt__card' + (red ? ' is-red' : '')} aria-label={`${title(card.rank)} of ${card.suit}`}><span className="ff-dt__card-index">{rankLabels[card.rank]}<b>{suitLabels[card.suit]}</b></span><span className="ff-dt__card-suit" aria-hidden="true">{suitLabels[card.suit]}</span><span className="ff-dt__card-index ff-dt__card-index--bottom" aria-hidden="true">{rankLabels[card.rank]}<b>{suitLabels[card.suit]}</b></span></div> : <div className="ff-dt__card ff-dt__card--back" aria-label="Face down card"><span>FF</span></div>}</div><span className="ff-dt__rank-label">{revealed && card ? card.rank === 'ace' ? 'Ace · 1' : card.rank === 'jack' ? 'Jack · 11' : card.rank === 'queen' ? 'Queen · 12' : card.rank === 'king' ? 'King · 13' : rankLabels[card.rank] : 'HIGH CARD'}</span></section>
}
function ChipStack({ placements }: { placements: Placement[] }) {
  return <span className="ff-dt__stack" aria-label={`${placements.length} chips placed`}>{placements.slice(-12).map((p, i) => <img key={i} src={chips.find(chip => chip.value === p.value)?.image} alt="" draggable={false} style={{ '--dt-stack-index': i } as CSSProperties}/>)}{placements.length > 12 && <em>+{placements.length - 12}</em>}</span>
}
function Rules({ currencySymbol, status, onClose }: { currencySymbol: string; status: DragonTigerStatus | null; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close() }, [])
  return <dialog ref={dialog} className="ff-dt__rules" aria-labelledby="ff-dt-rules-title" onCancel={onClose} onClick={e => { if (e.target === e.currentTarget) onClose() }}><div><button type="button" className="ff-dt__close" aria-label="Close rules" onClick={onClose}>×</button><span className="ff-dt__eyebrow">THE TABLE GUIDE</span><h2 id="ff-dt-rules-title">One card. Two contenders.</h2><p>Choose a chip, then click Dragon, Tiger, or Tie. Each click adds a chip to that spot. Bet on more than one spot if you wish, then select <strong>Deal cards</strong>. You play at your own pace.</p><h3>Which card wins?</h3><p>One card is dealt to Dragon, then one to Tiger from a shuffled shoe of 8 standard decks (416 cards). The higher rank wins. <strong>Ace is 1</strong>, then 2–10, Jack 11, Queen 12, King 13. Suits have no ranking. Equal ranks are a Tie, including equal cards from different decks.</p><table><thead><tr><th>Your bet</th><th>Winning payout</th><th>When cards tie</th></tr></thead><tbody><tr><td>Dragon</td><td>1:1</td><td>Half your bet returned</td></tr><tr><td>Tiger</td><td>1:1</td><td>Half your bet returned</td></tr><tr><td>Tie</td><td>8:1</td><td>Wins</td></tr></tbody></table><p>Payouts show profit, plus your winning stake is returned. A {money(1, currencySymbol)} winning Dragon bet returns {money(2, currencySymbol)}; a winning Tie bet returns {money(9, currencySymbol)}. A {money(1, currencySymbol)} Dragon bet on a tied hand returns {money(.5, currencySymbol)}. Other losing bets return zero.</p><h3>Your table & wallet</h3><p>{status ? `${money(status.minimumStake, currencySymbol)} minimum per spot, in ${money(status.stakeIncrement, currencySymbol)} increments. ${money(status.maximumStake, currencySymbol)} maximum per spot and ${money(status.maximumTotalStake, currencySymbol)} total per round.` : 'Table limits are shown above the cards once connected.'} Bet is the total placed; Balance is your settled wallet; Win is positive net profit for the hand. Returned includes winning stakes and any half returned on a tie; Net includes every spot wagered.</p><p>The server draws and settles each deal. If the connection drops, restore the pending deal using its original key before placing a new wager. Undo removes the last chip. Rebet repeats your previous wager without dealing. Recent results are a record, not a prediction.</p><button type="button" className="ff-dt__deal" onClick={onClose}>Back to the table →</button></div></dialog>
}
function reconstruct(round: DragonTigerRound): Placement[] {
  const placements: Placement[] = []
  for (const bet of round.bets) { let cents = Math.round(bet.stake * 100); for (const chip of [...chips].reverse()) while (cents >= chip.value * 100) { placements.push({ side: bet.side, value: chip.value }); cents -= chip.value * 100 } }
  return placements
}
function readStorage(key: string) { try { return localStorage.getItem(key) } catch { return null } }
function writeStorage(key: string, value: string) { try { localStorage.setItem(key, value) } catch { /* Memory recovery remains available when storage is restricted. */ } }
function removeStorage(key: string) { try { localStorage.removeItem(key) } catch { /* optional browser storage */ } }
function readPending(key: string): Pending | null {
  try {
    const v = JSON.parse(readStorage(key) ?? 'null') as Pending | null
    if (!v || typeof v.key !== 'string' || !/^dragon-tiger-[a-zA-Z0-9-]{16,100}$/.test(v.key) || !v.stakes || !Array.isArray(v.placements) || v.placements.length > 200) return null
    if (!dragonTigerSides.every(side => Number.isFinite(v.stakes[`${side}Stake`]) && v.stakes[`${side}Stake`] >= 0 && Number.isInteger(v.stakes[`${side}Stake`] * 2))) return null
    if (!v.placements.every(p => dragonTigerSides.includes(p.side) && chips.some(c => c.value === p.value))) return null
    const derived = stakesFor(v.placements)
    return sum(derived) > 0 && dragonTigerSides.every(side => derived[`${side}Stake`] === v.stakes[`${side}Stake`]) ? v : null
  } catch { return null }
}
function readHistory(key: string): HistoryItem[] {
  try { const v = JSON.parse(readStorage(key) ?? '[]'); return Array.isArray(v) ? v.filter(item => typeof item?.id === 'string' && dragonTigerSides.includes(item.outcome)).slice(-36) : [] } catch { return [] }
}

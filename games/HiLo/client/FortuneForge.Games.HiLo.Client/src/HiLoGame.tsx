import { useEffect, useId, useRef, useState } from 'react'
import '@fontsource-variable/sora'
import { HiLoGatewayError, hiLoRanks, type HiLoCard, type HiLoChoice, type HiLoGateway, type HiLoQuote, type HiLoSession } from './contracts'
import { HttpHiLoGateway } from './httpHiLoGateway'
import { createHiLoOperationId, HiLoPendingOperationStore, recoverHiLoPendingCommand, submitHiLoCommand, type HiLoPendingCommand } from './hiLoPendingCommand'
import { playHiLoAudio, readHiLoMuted, writeHiLoMuted } from './hiLoAudio'
import './hilo.css'

export type HiLoGameProps = Readonly<{ gateway?: HiLoGateway; playerId?: string; currencySymbol?: string; onBalanceChange?: (balance: number) => void; showTitle?: boolean }>
const defaultGateway = new HttpHiLoGateway()
const symbols = { clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' }
const ranks = ['A', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K']
const money = (cents: number, symbol: string) => `${symbol}${(cents / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
const multiplier = (value: number) => `${value.toLocaleString(undefined, { maximumFractionDigits: 3, minimumFractionDigits: 2 })}×`
const percent = (value: number) => `${(value * 100).toFixed(2)}%`
const cardName = (card: HiLoCard) => `${card.rank} of ${card.suit}`
const failure = (error: unknown) => error instanceof HiLoGatewayError ? error.message : 'The connection was interrupted. Restore your action to confirm the result.'

export function HiLoGame({ gateway = defaultGateway, playerId = 'standalone-practice', currencySymbol = 'R', onBalanceChange, showTitle = true }: HiLoGameProps) {
  const [session, setSession] = useState<HiLoSession | null>(null)
  const [wager, setWager] = useState('1.00')
  const [busy, setBusy] = useState(true)
  const [recovering, setRecovering] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [muted, setMuted] = useState(readHiLoMuted)
  const latestMuted = useRef(muted); latestMuted.current = muted
  const [rulesOpen, setRulesOpen] = useState(false)
  const [reveal, setReveal] = useState(0)
  const locked = useRef(true)
  const scope = useRef(0)
  const store = useRef<HiLoPendingOperationStore | null>(null)
  const balanceCallback = useRef(onBalanceChange); balanceCallback.current = onBalanceChange
  const modal = useRef<HTMLDialogElement>(null)
  const rulesButton = useRef<HTMLButtonElement>(null)
  const wagerId = useId()
  const round = session?.round ?? null
  const active = round?.status === 'active'
  const acceptingRisk = session?.enabled !== false
  const lost = round?.status === 'lost'
  const collected = round?.status === 'cashedOut' || round?.status === 'maximumReturn'
  const wagerCents = Math.round(Number(wager) * 100)
  const validWager = Number.isFinite(Number(wager)) && /^\d+(\.\d{0,2})?$/.test(wager) && wagerCents >= (session?.limits.minWagerCents ?? 10) && wagerCents <= (session?.limits.maxWagerCents ?? 100000)

  function accept(next: HiLoSession) {
    setSession(next)
    if (next.round) setWager((next.round.wagerCents / 100).toFixed(2))
    balanceCallback.current?.(next.walletBalanceCents / 100)
  }
  async function restore() {
    if (locked.current && !recovering && session) return
    locked.current = true; setBusy(true); setError(null)
    const currentScope = scope.current
    try {
      const journal = store.current ?? new HiLoPendingOperationStore(playerId)
      store.current = journal
      const next = await recoverHiLoPendingCommand(gateway, journal)
      if (scope.current !== currentScope) return
      accept(next); setRecovering(false); locked.current = false
    } catch (e) {
      if (scope.current !== currentScope) return
      setError(failure(e)); setRecovering(true)
    } finally { if (scope.current === currentScope) setBusy(false) }
  }
  useEffect(() => {
    const currentScope = ++scope.current
    const controller = new AbortController()
    locked.current = true; setBusy(true); setSession(null); setError(null); setRecovering(false)
    void (async () => {
      try {
        const journal = new HiLoPendingOperationStore(playerId); store.current = journal
        const next = await recoverHiLoPendingCommand(gateway, journal, controller.signal)
        if (controller.signal.aborted || scope.current !== currentScope) return
        accept(next); locked.current = false
      } catch (e) {
        if (controller.signal.aborted || scope.current !== currentScope) return
        setError(failure(e)); setRecovering(true)
      } finally { if (!controller.signal.aborted && scope.current === currentScope) setBusy(false) }
    })()
    return () => { controller.abort(); scope.current++ }
  }, [gateway, playerId])

  useEffect(() => {
    if (rulesOpen) modal.current?.showModal()
    else if (modal.current?.open) modal.current.close()
  }, [rulesOpen])

  async function submit(choice?: HiLoChoice) {
    if (locked.current || !session || !store.current || rulesOpen || (choice ? !active : active)) return
    if (!acceptingRisk && choice !== 'cashout') return
    if (!choice && (!validWager || wagerCents > session.walletBalanceCents)) {
      setError(validWager ? 'Your wallet has insufficient credits for this wager.' : `Enter a wager from ${money(session.limits.minWagerCents, currencySymbol)} to ${money(session.limits.maxWagerCents, currencySymbol)}, in whole cents.`)
      return
    }
    const currentScope = scope.current
    locked.current = true; setBusy(true); setError(null)
    playHiLoAudio(choice === 'cashout' ? 'collect' : choice ? 'move' : 'shuffle', muted)
    try {
      const command: HiLoPendingCommand = choice && round
        ? { kind: 'action', operationId: createHiLoOperationId(), roundId: round.roundId, expectedRevision: round.revision, choice }
        : { kind: 'start', operationId: createHiLoOperationId(), wagerCents }
      const next = await submitHiLoCommand(gateway, store.current, command)
      if (scope.current !== currentScope) return
      accept(next); setReveal(value => value + 1); locked.current = false
      if (choice !== 'cashout') playHiLoAudio('deal', latestMuted.current)
    } catch (e) {
      if (scope.current !== currentScope) return
      setError(failure(e))
      try {
        const pending = store.current.read()
        if (pending) setRecovering(true)
        else {
          const latest = await gateway.getSession()
          if (scope.current !== currentScope) return
          accept(latest); locked.current = false
        }
      } catch { if (scope.current === currentScope) setRecovering(true) }
    } finally { if (scope.current === currentScope) setBusy(false) }
  }

  function changeWager(factor: number) {
    if (!session || busy || active || recovering) return
    setWager((Math.max(session.limits.minWagerCents, Math.min(session.limits.maxWagerCents, Math.floor(wagerCents * factor) || session.limits.minWagerCents)) / 100).toFixed(2))
    setError(null)
  }
  const notice = busy ? (session ? 'Confirming your action…' : 'Opening the table…') : recovering ? 'Your action needs to be restored' : !acceptingRisk ? 'Table paused · cash out your active return' : lost ? (round.history.at(-2)?.card.rank === round.currentCard.rank ? 'Same rank · round lost' : 'Missed this one · round lost') : collected ? (round.status === 'maximumReturn' ? 'Limit reached · return collected' : 'Return collected') : active ? (round.streak ? 'Correct call. Collect or keep going.' : 'Your card is dealt. Make your call.') : 'Set your wager. Make your first move.'

  return <section className="ff-hilo" aria-label="Hi-Lo card game" aria-busy={busy}>
    <aside className="hilo-rail">
      <div className="hilo-rail-heading"><span className="hilo-eyebrow">FORTUNE FORGE ORIGINALS</span><span className="hilo-rail-game">Hi-Lo <i>♦</i></span></div>
      <div className="hilo-wallet"><span>{session?.practice ? 'Practice balance' : 'Wallet balance'}</span><strong data-testid="hilo-balance">{session ? money(session.walletBalanceCents, currencySymbol) : '—'}</strong>{session?.practice && <small>Practice credits · no cash value</small>}</div>
      <div className="hilo-bet-controls">
        <label htmlFor={wagerId}>Wager <span>{currencySymbol}</span></label>
        <div className="hilo-wager-input"><input id={wagerId} aria-label="Wager amount" inputMode="decimal" value={wager} disabled={busy || active || recovering || !session || !acceptingRisk} onChange={event => { setWager(event.target.value); setError(null) }} onBlur={() => { if (validWager) setWager((wagerCents / 100).toFixed(2)) }} /><button aria-label="Half wager" disabled={busy || active || recovering || !session || !acceptingRisk} onClick={() => changeWager(.5)}>½</button><button aria-label="Double wager" disabled={busy || active || recovering || !session || !acceptingRisk} onClick={() => changeWager(2)}>2×</button></div>
        <button className="hilo-start" disabled={busy || active || recovering || !session || !acceptingRisk} onClick={() => void submit()}>{busy && !session ? 'Connecting…' : active ? 'Wager in play' : round ? 'Play again' : 'Place wager'}<span>{active ? money(round.wagerCents, currencySymbol) : 'Deal my card'}</span></button>
      </div>
      <div className="hilo-rail-info"><span>ONE CARD. YOUR CALL.</span><p>Go higher or lower.<br />Build your streak.<br />Collect when you’re ready.</p><div className="hilo-rank-key"><b>A</b><span>LOW</span><i>→</i><span>HIGH</span><b>K</b></div></div>
      <div className="hilo-utilities"><button ref={rulesButton} onClick={() => setRulesOpen(true)} aria-label="How to play Hi-Lo"><InfoIcon /><span>How to play</span></button><button aria-label={muted ? 'Turn sound on' : 'Mute sound'} aria-pressed={muted} onClick={() => { setMuted(!muted); writeHiLoMuted(!muted); playHiLoAudio('click', !muted) }}><SoundIcon muted={muted} /><span>Sound {muted ? 'off' : 'on'}</span></button></div>
    </aside>
    <div className={`hilo-table${lost ? ' hilo-table--lost' : ''}${collected ? ' hilo-table--collected' : ''}`}>
      <header className="hilo-table-header"><span className="hilo-table-tag">HIGHER / LOWER</span>{showTitle && <h1>HI<span>♦</span>LO</h1>}<div className="hilo-streak" aria-label={`${round?.streak ?? 0} correct guesses`}><span>STREAK</span><strong>{String(round?.streak ?? 0).padStart(2, '0')}</strong></div></header>
      <div className="hilo-arena">
        <div className="hilo-multiplier"><span>{collected ? 'COLLECTED AT' : 'CURRENT MULTIPLIER'}</span><strong data-testid="hilo-multiplier">{multiplier(round?.multiplier ?? 1)}</strong></div>
        <div className="hilo-card-row">
          <ChoiceButton direction="lower" quote={round?.lower} disabled={busy || !active || recovering || !acceptingRisk} symbol={currencySymbol} onChoose={() => void submit('lower')} />
          <div className="hilo-card-stage"><div className="hilo-deck-shadow" aria-hidden="true" /><div key={`${round?.roundId}-${round?.revision}-${reveal}`} className={`hilo-card-reveal${round ? ' hilo-card-reveal--face' : ''}`}><Card card={round?.currentCard} /></div>{active && <button className="hilo-skip" aria-label={`Skip card, ${session!.limits.maxSkips - round.skips} skips remaining`} disabled={busy || recovering || !round.canSkip || !acceptingRisk} onClick={() => void submit('skip')}><SkipIcon /><span>Skip</span></button>}{(lost || collected) && <div className={`hilo-result-seal ${lost ? 'is-lost' : 'is-collected'}`} aria-hidden="true">{lost ? 'ROUND OVER' : 'COLLECTED'}</div>}</div>
          <ChoiceButton direction="higher" quote={round?.higher} disabled={busy || !active || recovering || !acceptingRisk} symbol={currencySymbol} onChoose={() => void submit('higher')} />
        </div>
        <div className="hilo-trail" role="list" aria-label="Revealed cards in this round">{round ? round.history.slice(-9).map((entry, index) => <div className={`hilo-trail-item${entry.won === false ? ' is-loss' : entry.won ? ' is-win' : ''}`} role="listitem" key={`${round.history.length - 9 + index}`} aria-label={`${cardName(entry.card)}, ${entry.choice}${entry.won === null ? '' : entry.won ? ', correct' : ', lost'}`}><span className={entry.card.suit === 'hearts' || entry.card.suit === 'diamonds' ? 'is-red' : ''}>{ranks[hiLoRanks.indexOf(entry.card.rank)]}<i>{symbols[entry.card.suit]}</i></span><small>{entry.choice === 'start' ? 'START' : entry.choice === 'skip' ? 'SKIP' : entry.choice === 'higher' ? '↑' : '↓'}</small></div>) : <span className="hilo-trail-empty">Your card journey starts here</span>}</div>
        <div className="hilo-collect-area"><button className="hilo-collect" disabled={busy || recovering || !round?.canCashOut} onClick={() => void submit('cashout')}><span>{collected ? 'Collected' : 'Cash out'}</span><strong data-testid="hilo-return">{money(round?.currentReturnCents ?? 0, currencySymbol)}</strong><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14m-5-5 5 5-5 5" /></svg></button><span className="hilo-return-label">{active && round.streak === 0 ? 'Your wager can be returned before guessing' : collected ? 'Added to your wallet' : 'Total return includes your wager'}</span></div>
      </div>
      <div className={`hilo-notice${error ? ' hilo-notice--error' : ''}`} role={error ? 'alert' : 'status'} aria-live="polite"><span>{error ?? notice}</span>{recovering && <button disabled={busy} onClick={() => void restore()}>{busy ? 'Restoring…' : 'Restore action'}</button>}</div>
      <footer className="hilo-table-footer"><span>ACE LOW <i>•</i> KING HIGH <i>•</i> TIES LOSE</span><span>1% edge per uncapped choice</span></footer>
    </div>
    <dialog className="hilo-rules" ref={modal} onCancel={() => setRulesOpen(false)} onClose={() => { setRulesOpen(false); rulesButton.current?.focus() }} aria-labelledby={`${wagerId}-rules-title`}><button className="hilo-rules-close" onClick={() => setRulesOpen(false)} aria-label="Close rules">×</button><span className="hilo-eyebrow">THE RULES</span><h2 id={`${wagerId}-rules-title`}>One card. Higher or lower.</h2><ol><li>Place a wager to receive your starting card.</li><li>Predict whether the next card will be strictly higher or lower. Ace is lowest, King highest. An equal rank loses.</li><li>Each correct call increases your total return. A wrong call ends the round and loses the wager and accumulated return.</li><li>Cash out any time while the round is active. Before your first guess, cashing out returns your wager.</li></ol><h3>The odds are on the table</h3><p>Every card is an independent draw from a standard 52-card deck, with replacement. Each rank has a 1 in 13 chance. With rank r (Ace = 1), higher wins on 13 − r ranks and lower on r − 1 ranks. Suits do not affect the outcome.</p><p>The uncapped step multiplier is 0.99 divided by your win probability. The 1% edge applies to <strong>each risked choice</strong>; it compounds through a streak. For example, higher from 7 wins 6/13 of the time and pays 2.145× before caps. The amount displayed on each button is the total return for a correct call.</p><p>You may skip {session?.limits.maxSkips ?? 52} times per round without changing your streak or return. The next draw remains independent. An impossible choice is disabled.</p><p>Maximum return is the smaller of {session?.limits.maxMultiplier ?? 1000}× your wager and {money(session?.limits.maximumReturnCents ?? 10000000, currencySymbol)}. Reaching that limit or {session?.limits.maxStreak ?? 100} correct calls automatically cashes out. Caps reduce an offered return; final returns are rounded down to whole cents. Both can increase the effective edge.</p><p>{session?.practice ? 'You are playing with practice credits. They have no cash value.' : 'Your wallet is debited once when a wager starts. A collected return is credited once.'} Refreshing restores your active round. If a connection fails, restore the pending action before playing again.</p><button className="hilo-start" onClick={() => setRulesOpen(false)}>Let’s play</button></dialog>
  </section>
}

function ChoiceButton({ direction, quote, disabled, symbol, onChoose }: { direction: 'higher' | 'lower'; quote?: HiLoQuote; disabled: boolean; symbol: string; onChoose: () => void }) {
  const available = quote?.available === true
  return <div className={`hilo-choice-wrap hilo-choice-wrap--${direction}`}><span className="hilo-choice-label">{direction === 'higher' ? 'HIGHER' : 'LOWER'}</span><button className="hilo-choice" aria-label={`Choose ${direction}${quote && available ? `, ${percent(quote.probability)} chance, return ${money(quote.projectedReturnCents, symbol)}` : ''}`} disabled={disabled || !available} onClick={onChoose}><svg viewBox="0 0 32 32" aria-hidden="true"><path d={direction === 'higher' ? 'M16 26V6M7 15l9-9 9 9' : 'M16 6v20M7 17l9 9 9-9'} /></svg><strong>{available ? percent(quote.probability) : quote ? '—' : '—'}</strong></button><div className="hilo-choice-return"><strong>{available ? multiplier(quote.multiplier) : '—'}</strong><span>{available ? money(quote.projectedReturnCents, symbol) : 'NO CALL'}</span></div></div>
}
function Card({ card }: { card?: HiLoCard }) {
  if (!card) return <div className="hilo-playing-card hilo-playing-card--back" aria-label="Face-down starting card"><div><span>♦</span><b>F</b><i>FORTUNE FORGE</i></div></div>
  const rank = ranks[hiLoRanks.indexOf(card.rank)], suit = symbols[card.suit]
  return <div className={`hilo-playing-card${card.suit === 'hearts' || card.suit === 'diamonds' ? ' is-red' : ''}`} role="img" aria-label={cardName(card)}><div className="hilo-card-corner"><b>{rank}</b><span>{suit}</span></div><div className="hilo-card-center" aria-hidden="true">{hiLoRanks.indexOf(card.rank) >= 10 && <b>{rank}</b>}<span>{suit}</span></div><div className="hilo-card-corner hilo-card-corner--bottom" aria-hidden="true"><b>{rank}</b><span>{suit}</span></div></div>
}
function InfoIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 11v6m0-11v1" /></svg> }
function SkipIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m4 5 9 7-9 7V5Zm11 0v14m5-14v14" /></svg> }
function SoundIcon({ muted }: { muted: boolean }) { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M11 4 5 9H2v6h3l6 5V4Z" />{muted ? <path d="m16 9 6 6m0-6-6 6" /> : <path d="M15 8q5 4 0 8m3-12q9 8 0 16" />}</svg> }

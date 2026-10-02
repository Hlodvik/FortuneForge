import { useEffect, useRef, useState } from 'react'
import type { LiarsDiceGateway } from './contracts'
import { DiceThrow } from './DiceThrow'
import { bidLabel, nextBid, outcomeLabel, playerLabel, validBid } from './liarsDiceHelpers'
import { useLiarsDiceTable } from './useLiarsDiceTable'

export type LiarsDiceGameProps = Readonly<{ gateway: LiarsDiceGateway; playerId?: string; showTitle?: boolean }>
export function LiarsDiceGame({ gateway, playerId, showTitle = true }: LiarsDiceGameProps) {
  const table = useLiarsDiceTable(gateway, playerId)
  const { match, pendingMatch, status, busy, rolling, error, recovery, history } = table
  const [dicePerPlayer, setDicePerPlayer] = useState(5)
  const [quantity, setQuantity] = useState('1')
  const [face, setFace] = useState(1)
  const [draftContext, setDraftContext] = useState('')
  const acceptedBidKey = match ? JSON.stringify([match.matchId, match.roundNumber, match.currentBid, match.totalDice]) : ''
  const [hidden, setHidden] = useState(false)
  const [panel, setPanel] = useState<'Rules' | 'History' | 'Setup' | null>(null)
  const returnFocus = useRef<HTMLElement | null>(null)
  const closeButton = useRef<HTMLButtonElement | null>(null)
  const primaryButton = useRef<HTMLButtonElement | null>(null)
  const challengeButton = useRef<HTMLButtonElement | null>(null)
  const recoveryButton = useRef<HTMLButtonElement | null>(null)
  const actionFocus = useRef<HTMLButtonElement | null>(null)
  useEffect(() => {
    const target = actionFocus.current
    if (busy || !target || match?.phase === 'bidding' && draftContext !== acceptedBidKey) return
    actionFocus.current = null
    if (document.activeElement === document.body || document.activeElement === target) {
      if (target.isConnected && !target.disabled) target.focus()
      else [primaryButton.current, challengeButton.current, recoveryButton.current].find(button => button && !button.disabled)?.focus()
    }
  }, [busy, match, draftContext, acceptedBidKey])
  function perform(target: HTMLButtonElement, operation: () => void) { actionFocus.current = target; operation() }
  useEffect(() => { if (status) setDicePerPlayer(status.startingDicePerPlayer) }, [status?.startingDicePerPlayer])
  useEffect(() => {
    if (!match) return
    const next = nextBid(match.currentBid, match.totalDice)
    setQuantity(next ? String(next.quantity) : ''); setFace(next?.face ?? 6); setDraftContext(acceptedBidKey)
  }, [acceptedBidKey])
  useEffect(() => {
    if (!match?.opponentsThinking || busy || recovery !== 'ready' || status?.available !== true) return
    const timer = window.setTimeout(() => table.advance(), 200)
    return () => window.clearTimeout(timer)
  }, [busy, match?.matchId, match?.roundNumber, match?.currentPlayerId, match?.currentBid?.quantity, match?.currentBid?.face, match?.opponentsThinking, recovery, status?.available])
  useEffect(() => { setHidden(false); setPanel(null) }, [match?.matchId, match?.roundNumber, playerId])
  useEffect(() => { if (panel) closeButton.current?.focus() }, [panel])
  const human = match?.players.find(p => p.isHuman)
  const yourTurn = !!match && match.phase === 'bidding' && !match.winner && match.currentPlayerId === human?.id && human.active
  const available = !busy && status?.available === true && recovery === 'ready'
  const draft = { quantity: Number(quantity), face }
  const legal = !!match && quantity.trim() !== '' && validBid(draft, match.currentBid, match.totalDice)
  const minimum = match ? nextBid(match.currentBid, match.totalDice) : null
  const canCall = available && yourTurn && !!match?.currentBid
  const heading = rolling ? 'Rolling…' : match ? match.outcome ? outcomeLabel(match) : yourTurn ? 'Your turn' : playerLabel(match, match.currentPlayerId) + ' to act' : 'Ready to play'
  function open(next: typeof panel, button: HTMLElement) { returnFocus.current = button; setPanel(next) }
  function close() { setPanel(null); returnFocus.current?.focus() }
  function start() { close(); table.start(dicePerPlayer) }
  const primaryText = !match ? busy ? rolling ? 'Rolling…' : 'Opening…' : 'New match' : match.winner ? 'Rematch' : match.phase === 'resolved' ? 'Next round' : yourTurn ? minimum ? 'Place bid' : 'Maximum bid' : match.opponentsThinking ? 'Waiting' : 'Continue'
  function primary() { if (!match || match.winner) table.start(dicePerPlayer); else if (match.phase === 'resolved') table.nextRound(); else if (yourTurn) table.bid(draft); else table.advance() }
  const primaryDisabled = !available || !!match && (yourTurn && !legal || match.opponentsThinking)
  const shownHand = (pendingMatch ?? match)?.hand ?? []
  const startingOptions = [...new Set([3, 5, 6, status?.startingDicePerPlayer ?? 5])].sort((a, b) => a - b)
  const cupLabel = match?.phase === 'resolved' ? 'Your last cup' : 'Your cup'
  return <div className={'ff-liars-page' + (match ? ' has-match' : '')}><main className="ff-liars-main" aria-busy={busy}>
    <header className="ff-liars-title">
      {showTitle && <h1>Liar's Dice</h1>}
      <span className="ff-liars-round">{match ? 'Round ' + match.roundNumber + ' · ' + match.totalDice + ' dice' : ''}</span>
      <nav aria-label="Table options">{(['Rules', 'History', 'Setup'] as const).map(name => <button type="button" key={name} onClick={event => open(name, event.currentTarget)} aria-expanded={panel === name}>{name}</button>)}</nav>
    </header>
    <section className="ff-liars-table" aria-label="Liar's Dice table">
      <div className="ff-liars-felt">
        <div className="ff-liars-players">{(match?.players ?? [
          { id:'you', displayName:'You', diceCount:dicePerPlayer, active:true, isHuman:true },
          ...['Amber Badger','Copper Finch','Silver Otter'].map((displayName, i) => ({ id:'seat-' + i, displayName, diceCount:dicePerPlayer, active:true, isHuman:false }))
        ]).map((p, index) => <div className={'ff-liars-player seat-' + index + (match?.phase === 'bidding' && p.active && p.id === match.currentPlayerId ? ' is-turn' : '') + (p.isHuman ? ' is-human' : '') + (!p.active ? ' is-out' : '') + (p.id === match?.outcome?.loserId ? ' lost-die' : '') + (p.id === match?.winner ? ' is-winner' : '')} key={p.id}>
          <span className="ff-liars-seat-dot" aria-hidden="true" /><strong title={p.displayName}>{p.isHuman ? 'You' : p.displayName}</strong>
          <span className="ff-liars-dice-count" aria-label={p.displayName + ': ' + p.diceCount + ' dice'}>{p.diceCount}<span aria-hidden="true">◆</span></span>
          <small>{p.id === match?.winner ? 'Winner' : !p.active ? 'Out' : p.id === match?.outcome?.loserId ? '−1 die' : match?.phase === 'bidding' && p.id === match.currentPlayerId ? 'Turn' : ' '}</small>
        </div>)}</div>
        <div className="ff-liars-bid-board">
          <div className="ff-liars-bid-heading">{match?.currentBid ? playerLabel(match, match.currentBidderId!) + ' bids' : 'Opening bid'}</div>
          <div className="ff-liars-bid-value" aria-label={match?.currentBid ? bidLabel(match.currentBid) : 'No bid yet'}><strong>{match?.currentBid?.quantity ?? '—'}</strong><span>×</span><PipDie face={match?.currentBid?.face ?? null} /></div>
          <div className="ff-liars-result" role="status" aria-live="polite">{heading}</div>
        </div>
        <section className="ff-liars-hand-section" aria-label={cupLabel}>
          <header><strong>{cupLabel}</strong><button type="button" onClick={() => setHidden(value => !value)} disabled={!match || rolling || !shownHand.length} aria-pressed={hidden}>{hidden ? 'Show dice' : 'Hide dice'}</button></header>
          <div className="ff-liars-hand">{hidden ? <span className="ff-liars-covered">Cup covered</span> : rolling ? <DiceThrow className="ff-liars-dice-stage" values={pendingMatch?.hand ?? Array.from({length:shownHand.length || dicePerPlayer}, () => null)} rollKey={pendingMatch ? pendingMatch.matchId + '-' + pendingMatch.roundNumber : 'rolling'} rolling={!pendingMatch} label="Rolling your private cup" /> : match && shownHand.length ? <DiceThrow className="ff-liars-dice-stage" values={shownHand} rollKey={match.matchId + '-' + match.roundNumber} label={'Your private dice: ' + shownHand.join(', ')} /> : <span className="ff-liars-covered">{match ? 'No dice remaining' : 'Your private cup'}</span>}</div>
        </section>
      </div>
      <section className="ff-liars-actions" aria-label="Liar's Dice actions">
        <div className="ff-liars-draft">
          {match && match.phase === 'bidding' ? <>
            <label className="ff-liars-quantity"><span>Quantity</span><div><button type="button" aria-label="Decrease quantity" disabled={!available || !yourTurn || !Number.isInteger(Number(quantity)) || Number(quantity) <= 1} onClick={() => setQuantity(String(Number(quantity) - 1))}>−</button><input type="number" aria-label="Bid quantity" min={1} max={match.totalDice} step={1} value={quantity} onChange={event => setQuantity(event.target.value)} disabled={!available || !yourTurn} /><button type="button" aria-label="Increase quantity" disabled={!available || !yourTurn || !Number.isInteger(Number(quantity)) || Number(quantity) >= match.totalDice} onClick={() => setQuantity(String(Number(quantity) + 1))}>+</button></div></label>
            <div className="ff-liars-faces" role="group" aria-label="Bid face">{[1,2,3,4,5,6].map(value => <button type="button" key={value} aria-label={'Bid face ' + value} aria-pressed={face === value} onClick={() => setFace(value)} disabled={!available || !yourTurn}><PipDie face={value} /></button>)}</div>
          </> : <div className="ff-liars-ready">{match ? null : <label>Starting dice<select aria-label="Starting dice" value={dicePerPlayer} onChange={event => setDicePerPlayer(Number(event.target.value))} disabled={!available}>{startingOptions.map(value => <option key={value} value={value}>{value}</option>)}</select></label>}</div>}
        </div>
        <div className="ff-liars-action-buttons"><button type="button" ref={primaryButton} className="ff-liars-primary" onClick={event => perform(event.currentTarget, primary)} disabled={primaryDisabled}>{busy && match ? rolling ? 'Rolling…' : 'Checking…' : primaryText}</button><button type="button" ref={challengeButton} className="ff-liars-challenge" onClick={event => perform(event.currentTarget, () => table.call('liar'))} disabled={!canCall}>Call liar</button><button type="button" className="ff-liars-spot-on" title="Call only when the exact bid count is correct." onClick={event => perform(event.currentTarget, () => table.call('spot-on'))} disabled={!canCall || !gateway.spotOn}>Spot on</button></div>
        <span className="ff-liars-bid-hint">{yourTurn ? legal ? bidLabel(draft) : minimum ? 'Raise quantity or face' : 'Call liar or spot on' : ' '}</span>
      </section>
    </section>
    <div className="ff-liars-notice">{error && <><span role="alert">{error}</span>{recovery === 'failed' || !status || !status.available ? <button type="button" ref={recoveryButton} onClick={table.retry} disabled={busy}>Check again</button> : null}</>}</div>
    {panel && <section className="ff-liars-details" role="dialog" aria-label={panel} onKeyDown={event => { if (event.key === 'Escape') { event.stopPropagation(); close() } }}>
      <header><h2>{panel}</h2><button type="button" ref={closeButton} onClick={close} aria-label="Close panel">×</button></header>
      <div className="ff-liars-details-body">{panel === 'Rules' ? <dl><dt>Bid</dt><dd>Raise the quantity, or raise the face at the same quantity. Only the exact face counts; ones are ordinary dice.</dd><dt>Call liar</dt><dd>If the count meets the bid, the caller loses one die. Otherwise the bidder loses one.</dd><dt>Spot on</dt><dd>An exact count makes the bidder lose one die. Otherwise the caller loses one.</dd><dt>Win</dt><dd>Last player with dice wins. Opponents’ cups stay private.</dd></dl> : panel === 'History' ? history.length ? <ol>{history.map(item => <li key={item.matchId + ':' + item.round}><b>Round {item.round}</b><span>{item.text}</span></li>)}</ol> : <p>No completed rounds.</p> : <><label>Starting dice<select aria-label="New match starting dice" value={dicePerPlayer} onChange={event => setDicePerPlayer(Number(event.target.value))} disabled={!available}>{startingOptions.map(value => <option key={value} value={value}>{value}</option>)}</select></label><button type="button" onClick={start} disabled={!available}>Start new match</button></>}</div>
    </section>}
  </main></div>
}
function PipDie({ face }: { face: number | null }) {
  const positions = face === 1 ? [5] : face === 2 ? [1,9] : face === 3 ? [1,5,9] : face === 4 ? [1,3,7,9] : face === 5 ? [1,3,5,7,9] : face === 6 ? [1,3,4,6,7,9] : []
  return <span className="ff-liars-pip-die" aria-hidden="true">{positions.map(position => <i key={position} style={{ gridArea: Math.ceil(position / 3) + ' / ' + ((position - 1) % 3 + 1) }} />)}{face === null && <span>?</span>}</span>
}

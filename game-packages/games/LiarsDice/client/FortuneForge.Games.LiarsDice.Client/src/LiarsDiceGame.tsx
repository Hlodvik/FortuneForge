import { useCallback, useEffect, useState } from 'react'
import { LiarsDiceGatewayError, type LiarsDiceGateway, type LiarsDiceMatch } from './contracts'
import { DiceThrow } from './DiceThrow'
import { bidLabel, nextBid, playerLabel } from './liarsDiceHelpers'
import './liarsDiceThrowTheme.css'

export type LiarsDiceGameProps = Readonly<{ gateway: LiarsDiceGateway; backHref?: string; playerName?: string; tableLabel?: string }>

export function LiarsDiceGame({ gateway, backHref = '/', playerName = 'Player', tableLabel = 'Local bot table' }: LiarsDiceGameProps) {
  const [status, setStatus] = useState<Awaited<ReturnType<LiarsDiceGateway['getStatus']>> | null>(null)
  const [match, setMatch] = useState<LiarsDiceMatch | null>(null)
  const [dicePerPlayer, setDicePerPlayer] = useState(5)
  const [quantity, setQuantity] = useState(1)
  const [face, setFace] = useState(1)
  const [busy, setBusy] = useState(false)
  const [rollingHand, setRollingHand] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const publish = useCallback((next: LiarsDiceMatch) => { setMatch(next); setError(null) }, [])
  const run = useCallback(async (action: () => Promise<LiarsDiceMatch>) => { if (busy) return; setBusy(true); setError(null); try { publish(await action()) } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) } }, [busy, publish])
  const rollHand = useCallback(async (action: () => Promise<LiarsDiceMatch>) => {
    if (busy) return
    setBusy(true)
    setRollingHand(true)
    setError(null)
    try {
      const nextMatch = await action()
      publish(nextMatch)
      setRollingHand(false)
      await diceSettleDelay()
    } catch (reason) {
      setError(messageForError(reason))
    } finally {
      setRollingHand(false)
      setBusy(false)
    }
  }, [busy, publish])

  useEffect(() => {
    if (!match || match.phase !== 'bidding') return
    const suggested = nextBid(match.currentBid, match.totalDice)
    setQuantity(Math.min(match.totalDice, suggested.quantity))
    setFace(suggested.face)
  }, [match?.matchId, match?.roundNumber, match?.currentBid?.quantity, match?.currentBid?.face, match?.totalDice])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal)
      .then(nextStatus => { setStatus(nextStatus); return gateway.startMatch({ dicePerPlayer: 5 }, controller.signal) })
      .then(publish)
      .catch((reason: unknown) => { if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason)) })
    return () => controller.abort()
  }, [gateway, publish])

  const startMatch = useCallback(() => { void rollHand(() => gateway.startMatch({ dicePerPlayer })) }, [dicePerPlayer, gateway, rollHand])
  const serviceReady = status?.available === true
  const yourTurn = match?.currentPlayerId === 'you' && match.winner === null
  const canBid = match?.phase === 'bidding' && yourTurn
  const canChallenge = canBid && match.currentBid !== null

  return <div className="ff-liars-page">
    <header className="ff-liars-header"><a className="ff-liars-brand" href={backHref} aria-label="Fortune Forge home"><span aria-hidden="true">✦</span><strong>Fortune Forge</strong></a><a className="ff-liars-games" href={backHref}>Other games</a><div className="ff-liars-account"><strong>{playerName}</strong><span>{tableLabel}</span></div></header>
    <main className="ff-liars-main">
      <section className="ff-liars-title"><div><small>Exact-face dice game</small><h1>Liar's Dice</h1></div><div className="ff-liars-start"><label htmlFor="dice-count">Starting dice</label><select id="dice-count" value={dicePerPlayer} onChange={event => setDicePerPlayer(Number(event.target.value))} disabled={busy}><option value={3}>3</option><option value={5}>5</option><option value={6}>6</option></select><button type="button" onClick={startMatch} disabled={!serviceReady || busy}>{busy ? 'Rolling…' : 'New match'}</button></div></section>
      {match ? <>
        <section className="ff-liars-status" aria-live="polite"><div><small>Round {match.roundNumber}</small><strong>{phaseLabel(match)}</strong></div><div className="ff-liars-current"><small>{match.winner ? `${playerLabel(match, match.winner)} wins` : yourTurn ? 'Your turn' : `${playerLabel(match, match.currentPlayerId)} is acting`}</small><strong>{match.message}</strong></div><div><small>Dice in play</small><strong>{match.totalDice}</strong></div></section>

        <section className="ff-liars-table" aria-label="Liar's Dice table"><div className="ff-liars-players">{match.players.map(player => <div className={`ff-liars-player ${player.id === match.currentPlayerId ? 'is-turn' : ''} ${player.isHuman ? 'is-human' : ''} ${!player.active ? 'is-out' : ''}`} key={player.id}><div><strong>{player.displayName}</strong><small>{player.isHuman ? 'You' : player.active ? 'Bot' : 'Eliminated'}</small></div><span className="ff-liars-dice-count">{player.diceCount}<small>dice</small></span></div>)}</div><div className="ff-liars-bid-board"><small>Current bid</small><strong>{bidLabel(match.currentBid)}</strong>{match.currentBidderId && <span>by {playerLabel(match, match.currentBidderId)}</span>}<div className="ff-liars-bid-die" aria-hidden="true">{match.currentBid?.face ?? '?'}</div></div></section>

        <section className="ff-liars-hand-section" aria-labelledby="your-dice-title"><div className="ff-liars-section-heading"><div><small>Your dice</small><h2 id="your-dice-title">{match.hand.length ? `${match.hand.length} dice in your cup` : 'You are out of dice'}</h2></div><span>{match.hand.length ? 'Your dice are hidden from the bots.' : 'Watch the remaining players.'}</span></div><div className="ff-liars-hand">{match.hand.length > 0 && <DiceThrow className="ff-liars-dice-stage" values={match.hand} rollKey={`${match.matchId}-${match.roundNumber}`} rolling={rollingHand} label={`Your hidden dice show ${match.hand.join(', ')}`} />}</div></section>

        {match.outcome && <section className="ff-liars-outcome"><small>Challenge resolved</small><strong>{playerLabel(match, match.outcome.challengerId)} challenged {playerLabel(match, match.outcome.bidderId)}.</strong><span>There were {match.outcome.matchingDice} matching {match.outcome.face}s against a bid of {match.outcome.quantity}. <b>{playerLabel(match, match.outcome.loserId)}</b> loses one die.</span></section>}

        <section className="ff-liars-actions" aria-label="Liar's Dice actions">{canBid && <div className="ff-liars-action-card"><small>Raise the bid</small><div className="ff-liars-controls"><label><span>Quantity</span><input type="number" min={1} max={match.totalDice} value={quantity} onChange={event => setQuantity(Number(event.target.value))} disabled={busy} /></label><label><span>Face</span><select value={face} onChange={event => setFace(Number(event.target.value))} disabled={busy}>{[1, 2, 3, 4, 5, 6].map(value => <option value={value} key={value}>{value}s</option>)}</select></label><button type="button" onClick={() => void run(() => gateway.bid(match.matchId, { quantity, face }))} disabled={busy}>{busy ? 'Bidding…' : 'Place bid'}</button></div></div>}{canChallenge && <button className="ff-liars-challenge" type="button" onClick={() => void run(() => gateway.challenge(match.matchId))} disabled={busy}>{busy ? 'Checking…' : 'Call liar'}</button>}{match.phase === 'resolved' && !match.winner && <button className="ff-liars-primary" type="button" onClick={() => void rollHand(() => gateway.nextRound(match.matchId))} disabled={busy}>{busy ? 'Rolling…' : 'Start next round'}</button>}</section>
      </> : <div className="ff-liars-loading">{error ?? 'Rolling the first cups…'}</div>}
      {error && <div className="ff-liars-error" role="alert"><strong>{error}</strong><button type="button" onClick={startMatch} disabled={busy}>Try a new match</button></div>}
    </main>
  </div>
}

function phaseLabel(match: LiarsDiceMatch): string { return match.winner ? 'Match complete' : match.phase === 'bidding' ? 'Bidding' : 'Challenge resolved' }
function messageForError(reason: unknown): string { return reason instanceof LiarsDiceGatewayError ? reason.message : "The Liar's Dice table is unavailable. Start a new local match." }
function diceSettleDelay(): Promise<void> { const reducedMotion = typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches; return new Promise(resolve => window.setTimeout(resolve, reducedMotion ? 0 : 890)) }

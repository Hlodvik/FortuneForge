import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { useCardAudioClick } from './cardAudio'
import { HeartsGatewayError, type HeartsDifficulty, type HeartsGateway, type HeartsMatch, type HeartsScore } from './contracts'
import { botPersona, cardColor, directionLabel, legalCardRationale, seatLabel, turnLabel } from './heartsHelpers'

export type HeartsGameProps = Readonly<{ gateway: HeartsGateway }>

export function HeartsGame({ gateway }: HeartsGameProps) {
  const [status, setStatus] = useState<Awaited<ReturnType<HeartsGateway['getStatus']>> | null>(null)
  const [match, setMatch] = useState<HeartsMatch | null>(null)
  const [targetScore, setTargetScore] = useState(100)
  const [difficulty, setDifficulty] = useState<HeartsDifficulty>('standard')
  const [selectedPass, setSelectedPass] = useState<readonly string[]>([])
  const [revealedTrick, setRevealedTrick] = useState<HeartsMatch['recentTrick']>(null)
  const [showLastTrick, setShowLastTrick] = useState(false)
  const [scoreHistory, setScoreHistory] = useState<readonly Readonly<{ round: number; score: HeartsScore }>[]>([])
  const [stats, setStats] = useState(readHeartsStats)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const botPollInFlight = useRef(false)
  const scoreMatchId = useRef<string | null>(null)
  const recordedMatches = useRef(new Set<string>())
  const onCardAudioClick = useCardAudioClick()

  const publish = useCallback((next: HeartsMatch) => { setMatch(next); setError(null) }, [])
  const run = useCallback(async (action: () => Promise<HeartsMatch>) => {
    if (busy) return
    setBusy(true)
    setError(null)
    try { publish(await action()) } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }, [busy, publish])

  useEffect(() => {
    if (match?.phase !== 'passing') setSelectedPass([])
  }, [match?.phase, match?.roundNumber])

  useEffect(() => {
    const recent = match?.recentTrick ?? null
    if (!recent) { setRevealedTrick(null); return }
    setShowLastTrick(false)
    setRevealedTrick(recent)
    const timer = window.setTimeout(() => setRevealedTrick(current => current?.number === recent.number ? null : current), 1600)
    return () => window.clearTimeout(timer)
  }, [match?.matchId, match?.recentTrick?.number])

  useEffect(() => {
    if (!match) return
    if (scoreMatchId.current !== match.matchId) {
      scoreMatchId.current = match.matchId
      setScoreHistory([{ round: 0, score: { north: 0, east: 0, south: 0, west: 0 } }])
    }
    if (match.phase !== 'complete') return
    setScoreHistory(current => current.some(item => item.round === match.roundNumber)
      ? current
      : [...current, { round: match.roundNumber, score: match.score }])
  }, [match])

  useEffect(() => {
    if (!match?.winner || recordedMatches.current.has(match.matchId)) return
    recordedMatches.current.add(match.matchId)
    setStats(current => {
      const next = { played: current.played + 1, wins: current.wins + (match.winner === match.humanSeat ? 1 : 0) }
      writeHeartsStats(next)
      return next
    })
  }, [match])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal)
      .then(nextStatus => { setStatus(nextStatus); return gateway.startMatch({ targetScore: 100, difficulty: 'standard' }, controller.signal) })
      .then(publish)
      .catch((reason: unknown) => { if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason)) })
    return () => controller.abort()
  }, [gateway, publish])

  useEffect(() => {
    if (!match?.botsThinking) return
    const timer = window.setTimeout(() => {
      if (busy || botPollInFlight.current) return
      botPollInFlight.current = true
      setBusy(true)
      setError(null)
      void gateway.advance(match.matchId)
        .then(publish)
        .catch((reason: unknown) => setError(messageForError(reason)))
        .finally(() => {
          botPollInFlight.current = false
          setBusy(false)
        })
    }, 250)
    return () => window.clearTimeout(timer)
  }, [busy, gateway, match, publish])

  const startMatch = useCallback(() => {
    setShowLastTrick(false)
    void run(() => gateway.startMatch({ targetScore, difficulty }))
  }, [difficulty, gateway, run, targetScore])
  const togglePass = useCallback((code: string) => {
    if (busy || !match || match.phase !== 'passing' || match.submittedPasses.includes(match.humanSeat)) return
    setSelectedPass(current => current.includes(code) ? current.filter(value => value !== code) : current.length < 3 ? [...current, code] : current)
  }, [busy, match])

  const serviceReady = status?.available === true
  const canPass = match?.phase === 'passing' && !match.submittedPasses.includes(match.humanSeat)
  const canPlay = match?.phase === 'playing' && match.yourTurn
  const legalCodes = new Set(match?.legalCards.map(card => card.code) ?? [])
  const shownTrick = revealedTrick ?? (showLastTrick ? match?.recentTrick ?? null : null)

  return <div className="ff-hearts-page" onClickCapture={onCardAudioClick}>
    <main className="ff-hearts-main">
      <section className="ff-hearts-title"><div><small>Take the fewest points to win</small><h1>Hearts</h1></div><div className="ff-hearts-start"><label htmlFor="target-score">Score limit</label><select id="target-score" value={targetScore} onChange={event => setTargetScore(Number(event.target.value))} disabled={busy} aria-label="Score limit; the player with the lowest score wins when someone reaches this total"><option value={50}>50</option><option value={100}>100</option><option value={150}>150</option></select><label htmlFor="opponent-difficulty">Opponent level</label><select id="opponent-difficulty" value={difficulty} onChange={event => setDifficulty(event.target.value as HeartsDifficulty)} disabled={busy} aria-label="Opponent difficulty"><option value="relaxed">Relaxed</option><option value="standard">Standard</option><option value="sharp">Sharp</option></select><button type="button" data-card-audio="shuffle" onClick={startMatch} disabled={!serviceReady || busy}>{busy ? 'Dealing…' : 'New game'}</button></div></section>
      {match ? <>
        <section className="ff-hearts-scoreboard" aria-label="Hearts scoreboard">
          {match.players.map(player => <div className={`ff-hearts-score ${player.seat === match.turn ? 'is-turn' : ''} ${player.seat === match.humanSeat ? 'is-human' : ''}`} key={player.seat}><small>{player.seat === match.humanSeat ? 'You' : seatLabel(player.seat)}</small><strong>{player.matchScore}</strong><span>{player.roundScore} this round</span></div>)}
          <div className="ff-hearts-round"><small>Round {match.roundNumber} · {match.botsThinking ? 'Opponents thinking…' : turnLabel(match)}</small><strong>{phaseLabel(match)}</strong><span>{match.message}</span></div>
        </section>

        <section className="ff-hearts-table" aria-label="Hearts table">
          <div className="ff-hearts-table-head"><span className={`ff-hearts-phase phase-${match.phase}`}>{phaseLabel(match)}</span><span>Pass {directionLabel(match.passDirection)}</span><span>{match.heartsBroken ? '♥ Hearts broken' : '♥ Hearts not broken'}</span></div>
          {match.phase === 'passing' && <PassGuide direction={match.passDirection} />}
          <div className="ff-hearts-players">{match.players.map(player => <div className={`ff-hearts-player ${player.seat === match.turn ? 'is-turn' : ''}`} key={player.seat}><strong>{player.seat === match.humanSeat ? 'You' : seatLabel(player.seat)}</strong><span>{player.seat === match.humanSeat ? `${match.difficulty} table` : botPersona(player.seat)}</span><span>{player.handCount} cards · {player.hasPassed ? 'Passed' : 'Waiting'}</span></div>)}</div>
          <div className={`ff-hearts-trick ${revealedTrick ? 'is-revealing' : ''}`}><div className="ff-hearts-trick-heading"><small>{shownTrick ? `Trick ${shownTrick.number} complete · ${seatLabel(shownTrick.winner)} takes it` : `Current trick · led by ${seatLabel(match.currentTrick.leader)}`}</small><strong>{shownTrick ? `${shownTrick.points} point${shownTrick.points === 1 ? '' : 's'}` : `${match.currentTrick.plays.length}/4 cards`}</strong></div><div className="ff-hearts-trick-cards">{(shownTrick?.plays ?? match.currentTrick.plays).length === 0 ? <span className="ff-hearts-empty">The next trick will appear here.</span> : (shownTrick?.plays ?? match.currentTrick.plays).map(play => <div className="ff-hearts-trick-card" key={`${play.seat}-${play.card.code}`}><small>{seatLabel(play.seat)}</small><CardFace card={play.card} /></div>)}</div></div>
        </section>

        <section className="ff-hearts-hand-section" aria-labelledby="your-hand-title"><div className="ff-hearts-section-heading"><div><small>Your hand · North</small><h2 id="your-hand-title">{canPass ? `Choose 3 cards to pass ${directionLabel(match.passDirection).toLowerCase()}` : 'Choose your play'}</h2></div><span>{canPass ? `${selectedPass.length}/3 selected` : canPlay ? 'Select a highlighted legal card.' : match.botsThinking ? 'An opponent is thinking…' : match.phase === 'complete' ? 'Round over.' : `${seatLabel(match.turn)} is acting.`}</span></div><div className="ff-hearts-hand">{match.hand.map(card => <button className={`ff-hearts-card ${cardColor(card)} ${selectedPass.includes(card.code) ? 'is-selected' : ''} ${legalCodes.has(card.code) ? 'is-legal' : ''}`} type="button" key={card.code} data-card-audio={canPass ? 'move' : 'deal'} disabled={(!canPass && (!canPlay || !legalCodes.has(card.code))) || busy} onClick={() => canPass ? togglePass(card.code) : void run(() => gateway.playCard(match.matchId, card.code))} aria-label={`${card.label}${canPass ? selectedPass.includes(card.code) ? ', selected to pass' : ', select to pass' : legalCodes.has(card.code) ? ', play card' : ', not legal now'}`}><CardFace card={card} /></button>)}</div>{match.phase === 'playing' && <p className="ff-hearts-help">{legalCardRationale(match)}</p>}</section>

        <section className="ff-hearts-actions" aria-label="Hearts actions">{canPass && <button className="ff-hearts-primary" type="button" data-card-audio="move" disabled={busy || selectedPass.length !== 3} onClick={() => void run(() => gateway.pass(match.matchId, selectedPass))}>{busy ? 'Passing…' : `Pass ${selectedPass.length}/3 cards`}</button>}{match.recentTrick && !revealedTrick && <button className="ff-hearts-secondary" type="button" aria-pressed={showLastTrick} onClick={() => setShowLastTrick(value => !value)}>{showLastTrick ? 'Return to table' : 'Review last trick'}</button>}{match.phase === 'complete' && !match.winner && <button className="ff-hearts-primary" type="button" data-card-audio="shuffle" onClick={() => void run(() => gateway.nextRound(match.matchId))} disabled={busy}>{busy ? 'Dealing…' : 'Start next round'}</button>}{match.winner && <button className="ff-hearts-primary" type="button" data-card-audio="shuffle" onClick={startMatch} disabled={busy}>{busy ? 'Dealing…' : 'Rematch'}</button>}</section>

        <section className="ff-hearts-history" aria-label="Round details"><div><small>Round points</small><strong>{match.roundScore.north} · {match.roundScore.east} · {match.roundScore.south} · {match.roundScore.west}</strong></div><div><small>Tricks completed</small><strong>{match.completedTricks.length} / 13</strong></div><div><small>Point cards</small><strong>{match.completedTricks.reduce((total, trick) => total + trick.points, 0)} / 26</strong></div></section>
        <section className="ff-hearts-trajectory" aria-label="Score trajectory and table statistics"><div className="ff-hearts-trajectory__heading"><div><small>Score trajectory</small><strong>Lower is better</strong></div><span>{stats.wins} wins · {stats.played} finished matches</span></div><div className="ff-hearts-trajectory__rounds">{scoreHistory.map(item => <div key={item.round}><small>{item.round === 0 ? 'Start' : `R${item.round}`}</small>{(['north', 'east', 'south', 'west'] as const).map(seat => <span key={seat} title={`${seatLabel(seat)}: ${item.score[seat]}`} style={{ '--hearts-score-progress': `${Math.min(100, item.score[seat] / match.targetScore * 100)}%` } as CSSProperties}>{seat === 'north' ? 'You' : seatLabel(seat)[0]} {item.score[seat]}</span>)}</div>)}</div></section>
      </> : <div className="ff-hearts-loading">{error ?? 'Dealing the first hand…'}</div>}
      {error && <div className="ff-hearts-error" role="alert"><strong>{error}</strong><button type="button" onClick={startMatch} disabled={busy}>Try a new game</button></div>}
    </main>
  </div>
}

function CardFace({ card }: { card: HeartsMatch['hand'][number] }) { return <span className="ff-hearts-card-face" aria-label={card.label}><span>{card.label.slice(0, -1)}</span><b>{card.label.slice(-1)}</b></span> }
function PassGuide({ direction }: Readonly<{ direction: HeartsMatch['passDirection'] }>) {
  return <div className={`ff-hearts-pass-guide direction-${direction}`} role="img" aria-label={direction === 'hold' ? 'Hold your cards this round' : `Cards pass ${direction}`}><span>North</span><span>East</span><span>South</span><span>West</span><b aria-hidden="true">{direction === 'left' ? '↻' : direction === 'right' ? '↺' : direction === 'across' ? '↔' : '•'}</b></div>
}
function phaseLabel(match: HeartsMatch): string { return match.phase === 'passing' ? 'Passing' : match.phase === 'playing' ? 'Playing' : match.winner ? 'Game won' : 'Round complete' }
function messageForError(reason: unknown): string { return reason instanceof HeartsGatewayError ? reason.message : 'The Hearts table is unavailable. Start a new local game.' }
type HeartsStats = Readonly<{ played: number; wins: number }>
const heartsStatsKey = 'fortuneforge:hearts:stats:v1'
function readHeartsStats(): HeartsStats {
  try {
    const value = JSON.parse(localStorage.getItem(heartsStatsKey) ?? '') as Partial<HeartsStats>
    return Number.isInteger(value.played) && Number.isInteger(value.wins) ? { played: value.played!, wins: value.wins! } : { played: 0, wins: 0 }
  } catch { return { played: 0, wins: 0 } }
}
function writeHeartsStats(stats: HeartsStats) { try { localStorage.setItem(heartsStatsKey, JSON.stringify(stats)) } catch { /* local storage is optional */ } }

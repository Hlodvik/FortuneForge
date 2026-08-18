import { useEffect, useState } from 'react'
import {
  commandSolitaireBotPractice,
  getSolitaireBotPractice,
  joinSolitaireBotPractice,
  type SolitaireBotPracticeResponse,
} from '../../../games/cards/solitaire/botPracticeApi'
import { SolitaireBoard } from '../../../games/cards/solitaire/SolitaireBoard'
import { formatCountdown } from '../../../games/cards/solitaire/solitaireDisplay'
import type { SolitaireCommand } from '../../../games/cards/solitaire/solitaireTypes'
import { PracticeLobby, PracticeModeNotice, PracticeQueuePanel } from '../../../games/cards/shared/PracticeBotChrome'
import type { PracticeBotSkill } from '../../../games/cards/shared/practiceBots'
import { usePracticeBotSession } from '../../../games/cards/shared/usePracticeBotSession'
import '../../../games/cards/shared/playingCards.css'
import '../../../games/cards/shared/practiceBots.css'
import './solitaire.css'

export function SolitaireBotPracticePage() {
  const controller = usePracticeBotSession<SolitaireBotPracticeResponse>({ getSession: getSolitaireBotPractice })
  const [playerCount, setPlayerCount] = useState(4)
  const [skill, setSkill] = useState<PracticeBotSkill>(3)
  const [now, setNow] = useState(() => Date.now())
  const response = controller.state.kind === 'ready' ? controller.state.response : null
  const match = response?.match ?? null

  useEffect(() => {
    if (match === null) return
    const timer = window.setInterval(() => setNow(Date.now()), 1_000)
    return () => window.clearInterval(timer)
  }, [match])

  const join = () => void controller.mutate(
    `join:${playerCount}:${skill}`,
    (key) => joinSolitaireBotPractice(playerCount, skill, key),
  )
  const command = (next: SolitaireCommand) => {
    if (match === null) return
    void controller.mutate(
      `command:${match.matchId}:${match.version}:${JSON.stringify(next)}`,
      (key) => commandSolitaireBotPractice(match.matchId, match.version, next, key),
    )
  }

  return (
    <div className="practice-bot-page solitaire-page">
      <header className="practice-bot-header"><a href="/demo/cards">← Card room</a><span>Solitaire practice lab</span></header>
      <main className="practice-bot-main">
        <section className="practice-bot-hero"><p>Fortune Forge practice</p><h1>Solitaire Race</h1><span>Your board is private</span></section>
        <PracticeModeNotice />
        {controller.message && <div className="practice-bot-error" role="alert">{controller.message}</div>}
        {controller.state.kind === 'loading' && <div className="practice-bot-panel" role="status">Opening the practice race…</div>}
        {controller.state.kind === 'error' && <StatePanel title="Practice race unavailable" message={controller.state.message} onRetry={controller.refresh} />}
        {controller.state.kind === 'disabled' && <StatePanel title="Practice race is locked" message={controller.state.message} />}
        {controller.state.kind === 'idle' && (
          <PracticeLobby game="Solitaire" minimumPlayers={2} maximumPlayers={8} playerCount={playerCount} skill={skill} busy={controller.busy} disabled={false} onPlayerCountChange={setPlayerCount} onSkillChange={setSkill} onJoin={join} />
        )}
        {response?.queue && <PracticeQueuePanel queue={response.queue} />}
        {match && (
          <section className="practice-bot-table solitaire-match" aria-label="Account-neutral Solitaire practice match">
            <div className="practice-bot-table__status">
              <strong>Your board · v{match.version}</strong>
              <span>{formatCountdown(match.deadlineAtUtc, now)} remaining</span>
            </div>
            <div className="practice-bot-solitaire-opponents" aria-label="Race participants">
              {match.seats.map((seat) => (
                <span key={seat.seatId}>{seat.displayName}</span>
              ))}
            </div>
            <p>Other players’ boards and moves remain private. Only their names and final standings are shown.</p>
            <SolitaireBoard game={match.game} busy={controller.busy} onCommand={command} />
          </section>
        )}
        {response?.result && (
          <section className="practice-bot-panel" aria-labelledby="practice-solitaire-result-title">
            <p className="practice-bot-eyebrow">Final standings</p>
            <h2 id="practice-solitaire-result-title">Practice race complete</h2>
            <ol className="practice-bot-results">
              {response.result.standings.map((standing) => (
                <li key={standing.player.seatId}>
                  <strong>#{standing.rank} {standing.player.displayName}</strong>
                  <span>{standing.score.toLocaleString()} pts · {standing.moves} moves</span>
                </li>
              ))}
            </ol>
            <small>Synthetic results only. No payout or financial record was created.</small>
          </section>
        )}
      </main>
    </div>
  )
}

function StatePanel({ title, message, onRetry }: { title: string; message: string; onRetry?: () => void }) {
  return <section className="practice-bot-panel" role={onRetry ? 'alert' : 'status'}><h2>{title}</h2><p>{message}</p>{onRetry && <button type="button" onClick={onRetry}>Try again</button>}</section>
}

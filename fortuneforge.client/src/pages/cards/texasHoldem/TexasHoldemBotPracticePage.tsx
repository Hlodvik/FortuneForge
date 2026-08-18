import { useState } from 'react'
import {
  commandHoldemBotPractice,
  getHoldemBotPractice,
  joinHoldemBotPractice,
  type HoldemBotPracticeResponse,
  type HoldemPracticeCard,
} from '../../../games/cards/texasHoldem/botPracticeApi'
import { PracticeCard } from '../../../games/cards/shared/PracticeCard'
import { PracticeLobby, PracticeModeNotice, PracticeQueuePanel } from '../../../games/cards/shared/PracticeBotChrome'
import type { PracticeBotSkill } from '../../../games/cards/shared/practiceBots'
import { usePracticeBotSession } from '../../../games/cards/shared/usePracticeBotSession'
import '../../../games/cards/shared/playingCards.css'
import '../../../games/cards/shared/practiceBots.css'

export function TexasHoldemBotPracticePage() {
  const controller = usePracticeBotSession<HoldemBotPracticeResponse>({ getSession: getHoldemBotPractice })
  const [playerCount, setPlayerCount] = useState(4)
  const [skill, setSkill] = useState<PracticeBotSkill>(3)
  const response = controller.state.kind === 'ready' ? controller.state.response : null
  const table = response?.table ?? null

  const join = () => void controller.mutate(
    `join:${playerCount}:${skill}`,
    (key) => joinHoldemBotPractice(playerCount, skill, key),
  )
  const act = (action: 'fold' | 'check' | 'call' | 'raise') => {
    if (table === null) return
    void controller.mutate(
      `command:${table.matchId}:${table.version}:${action}`,
      (key) => commandHoldemBotPractice(
        table.matchId,
        table.version,
        action,
        key,
        action === 'raise' ? table.minimumRaiseTo : undefined,
      ),
    )
  }

  return (
    <div className="practice-bot-page">
      <header className="practice-bot-header"><a href="/demo/cards">← Card room</a><span>Hold’em practice lab</span></header>
      <main className="practice-bot-main">
        <section className="practice-bot-hero"><p>Fortune Forge practice</p><h1>Texas Hold’em</h1><span>Visible table action · private hole cards protected</span></section>
        <PracticeModeNotice />
        {controller.message && <div className="practice-bot-error" role="alert">{controller.message}</div>}
        {controller.state.kind === 'loading' && <div className="practice-bot-panel" role="status">Opening the table…</div>}
        {controller.state.kind === 'error' && <StatePanel title="Table unavailable" message={controller.state.message} onRetry={controller.refresh} />}
        {controller.state.kind === 'disabled' && <StatePanel title="Practice table is locked" message={controller.state.message} />}
        {controller.state.kind === 'idle' && (
          <PracticeLobby game="Texas Hold’em" minimumPlayers={2} maximumPlayers={6} playerCount={playerCount} skill={skill} busy={controller.busy} disabled={false} onPlayerCountChange={setPlayerCount} onSkillChange={setSkill} onJoin={join} />
        )}
        {response?.queue && <PracticeQueuePanel queue={response.queue} />}
        {table && (
          <section className="practice-bot-table" aria-label="Account-neutral Texas Hold’em practice table">
            <div className="practice-bot-table__status">
              <strong>{table.street} · pot {table.pot} practice chips</strong>
              <span>v{table.version} · current bet {table.currentBet}</span>
            </div>
            <section className="practice-bot-player">
              <h3>Community</h3>
              <CardRow cards={table.communityCards} />
            </section>
            <div className="practice-bot-player-grid">
              {table.seats.map((seat) => (
                <article className={`practice-bot-player${seat.player.seat === table.activeSeat ? ' is-active' : ''}`} key={seat.player.seatId}>
                  <h3>{seat.player.displayName}{seat.player.seat === table.dealerSeat ? ' · Dealer' : ''}</h3>
                  <CardRow cards={seat.holeCards} />
                  <div className="practice-bot-player__meta"><span>{seat.stack} chips</span><span>{seat.committed} committed</span></div>
                  <small>{seat.handName ?? seat.status}{seat.payout > 0 ? ` · won ${seat.payout}` : ''}</small>
                </article>
              ))}
            </div>
            <div className="practice-bot-actions" aria-label="Hold’em actions">
              {(['fold', 'check', 'call', 'raise'] as const).map((action) => (
                <button type="button" key={action} disabled={controller.busy || !table.legalActions.includes(action)} onClick={() => act(action)}>
                  {action === 'raise' ? `Raise to ${table.minimumRaiseTo}` : `${action[0].toUpperCase()}${action.slice(1)}`}
                </button>
              ))}
            </div>
            <ol className="practice-bot-event-list" aria-label="Public table activity">
              {table.events.slice(-10).reverse().map((event) => (
                <li key={`${event.version}-${event.actorSeatId}`}><strong>{event.actorDisplayName}</strong> {event.type}</li>
              ))}
            </ol>
          </section>
        )}
      </main>
    </div>
  )
}

function CardRow({ cards }: { cards: readonly HoldemPracticeCard[] }) {
  return <div className="practice-bot-cards">{cards.map((card, index) => <PracticeCard card={card} index={index} key={index} />)}</div>
}

function StatePanel({ title, message, onRetry }: { title: string; message: string; onRetry?: () => void }) {
  return <section className="practice-bot-panel" role={onRetry ? 'alert' : 'status'}><h2>{title}</h2><p>{message}</p>{onRetry && <button type="button" onClick={onRetry}>Try again</button>}</section>
}

import { useState } from 'react'
import {
  commandBlackjackBotPractice,
  getBlackjackBotPractice,
  joinBlackjackBotPractice,
  type BlackjackBotPracticeResponse,
  type BlackjackPracticeHand,
} from '../../../games/cards/blackjack/botPracticeApi'
import { PracticeCard } from '../../../games/cards/shared/PracticeCard'
import {
  PracticeLobby,
  PracticeModeNotice,
  PracticeQueuePanel,
} from '../../../games/cards/shared/PracticeBotChrome'
import type { PracticeBotSkill } from '../../../games/cards/shared/practiceBots'
import { usePracticeBotSession } from '../../../games/cards/shared/usePracticeBotSession'
import '../../../games/cards/shared/playingCards.css'
import '../../../games/cards/shared/practiceBots.css'

export function BlackjackBotPracticePage() {
  const controller = usePracticeBotSession<BlackjackBotPracticeResponse>({
    getSession: getBlackjackBotPractice,
  })
  const [playerCount, setPlayerCount] = useState(3)
  const [skill, setSkill] = useState<PracticeBotSkill>(3)
  const response = controller.state.kind === 'ready' ? controller.state.response : null
  const table = response?.table ?? null

  const join = () => void controller.mutate(
    `join:${playerCount}:${skill}`,
    (key) => joinBlackjackBotPractice(playerCount, skill, key),
  )
  const act = (action: 'hit' | 'stand' | 'double') => {
    if (table === null) return
    void controller.mutate(
      `command:${table.matchId}:${table.version}:${action}`,
      (key) => commandBlackjackBotPractice(table.matchId, table.version, action, key),
    )
  }

  return (
    <div className="practice-bot-page">
      <header className="practice-bot-header">
        <a href="/demo/cards">← Card room</a>
        <span>Blackjack practice lab</span>
      </header>
      <main className="practice-bot-main">
        <section className="practice-bot-hero">
          <p>Fortune Forge practice</p>
          <h1>Blackjack Table</h1>
          <span>Dealer stands on 17 · synthetic chips only</span>
        </section>
        <PracticeModeNotice />
        {controller.message && <div className="practice-bot-error" role="alert">{controller.message}</div>}
        {controller.state.kind === 'loading' && <div className="practice-bot-panel" role="status">Opening the table…</div>}
        {controller.state.kind === 'error' && <ErrorPanel message={controller.state.message} onRetry={controller.refresh} />}
        {controller.state.kind === 'disabled' && <DisabledPanel message={controller.state.message} />}
        {controller.state.kind === 'idle' && (
          <PracticeLobby
            game="Blackjack"
            minimumPlayers={2}
            maximumPlayers={6}
            playerCount={playerCount}
            skill={skill}
            busy={controller.busy}
            disabled={false}
            onPlayerCountChange={setPlayerCount}
            onSkillChange={setSkill}
            onJoin={join}
          />
        )}
        {response?.queue && <PracticeQueuePanel queue={response.queue} />}
        {table && (
          <section className="practice-bot-table" aria-label="Account-neutral Blackjack practice table">
            <div className="practice-bot-table__status">
              <strong>{table.status === 'completed' ? 'Round complete' : `Table state v${table.version}`}</strong>
              <span>{table.seats.length} seats · wagers are virtual units</span>
            </div>
            <PracticeHand label="Dealer" hand={table.dealer} />
            <div className="practice-bot-player-grid">
              {table.seats.map((seat) => (
                <article
                  className={`practice-bot-player${seat.player.seat === table.activeSeat ? ' is-active' : ''}`}
                  key={seat.player.seatId}
                >
                  <h3>{seat.player.displayName}</h3>
                  <div className="practice-bot-cards">
                    {seat.hand.cards.map((card, index) => <PracticeCard card={card} index={index} key={index} />)}
                  </div>
                  <div className="practice-bot-player__meta">
                    <span>{seat.hand.score ?? '—'} points</span>
                    <span>{seat.virtualWagerUnits} practice units</span>
                  </div>
                  <small>{seat.outcome ?? seat.player.status}</small>
                </article>
              ))}
            </div>
            <div className="practice-bot-actions" aria-label="Blackjack actions">
              {(['hit', 'stand', 'double'] as const).map((action) => (
                <button
                  type="button"
                  key={action}
                  disabled={controller.busy || !table.legalActions.includes(action)}
                  onClick={() => act(action)}
                >
                  {action[0].toUpperCase()}{action.slice(1)}
                </button>
              ))}
            </div>
            <EventList events={table.events} />
          </section>
        )}
      </main>
    </div>
  )
}

function PracticeHand({ label, hand }: { label: string; hand: BlackjackPracticeHand }) {
  return (
    <section className="practice-bot-player">
      <h3>{label}</h3>
      <div className="practice-bot-cards">
        {hand.cards.map((card, index) => <PracticeCard card={card} index={index} key={index} />)}
      </div>
      <small>{hand.score === null ? 'Hole card hidden' : `${hand.score} points`}</small>
    </section>
  )
}

function EventList({ events }: { events: readonly { version: number; type: string; actorSeatId: string; actorDisplayName: string }[] }) {
  return (
    <ol className="practice-bot-event-list" aria-label="Public table activity">
      {events.slice(-8).reverse().map((event) => (
        <li key={`${event.version}-${event.actorSeatId}`}>
          <strong>{event.actorDisplayName}</strong> {event.type}
        </li>
      ))}
    </ol>
  )
}

function DisabledPanel({ message }: { message: string }) {
  return <section className="practice-bot-panel" role="status"><h2>Practice table is locked.</h2><p>{message}</p></section>
}

function ErrorPanel({ message, onRetry }: { message: string; onRetry: () => void }) {
  return <section className="practice-bot-panel" role="alert"><h2>Table unavailable</h2><p>{message}</p><button type="button" onClick={onRetry}>Try again</button></section>
}

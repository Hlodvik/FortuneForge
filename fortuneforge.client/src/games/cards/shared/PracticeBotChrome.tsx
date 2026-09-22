import { PRACTICE_BOT_SKILLS, type PracticeBotSkill, type PracticeQueue } from './practiceBots'

export function PracticeLobby({
  game,
  minimumPlayers,
  maximumPlayers,
  playerCount,
  skill,
  busy,
  disabled,
  onPlayerCountChange,
  onSkillChange,
  onJoin,
}: {
  game: string
  minimumPlayers: number
  maximumPlayers: number
  playerCount: number
  skill: PracticeBotSkill
  busy: boolean
  disabled: boolean
  onPlayerCountChange: (value: number) => void
  onSkillChange: (value: PracticeBotSkill) => void
  onJoin: () => void
}) {
  const counts = Array.from(
    { length: maximumPlayers - minimumPlayers + 1 },
    (_, index) => minimumPlayers + index,
  )
  return (
    <section className="practice-bot-panel" aria-labelledby="practice-bot-lobby-title">
      <p className="practice-bot-eyebrow">Table setup</p>
      <h2 id="practice-bot-lobby-title">Open a {game} table</h2>
      <div className="practice-bot-options">
        <label>
          <span>Seats</span>
          <select
            value={playerCount}
            disabled={busy || disabled}
            onChange={(event) => onPlayerCountChange(Number(event.target.value))}
          >
            {counts.map((count) => <option value={count} key={count}>{count} players</option>)}
          </select>
        </label>
        <label>
          <span>Opponent strength</span>
          <select
            value={skill}
            disabled={busy || disabled}
            onChange={(event) => onSkillChange(Number(event.target.value) as PracticeBotSkill)}
          >
            {PRACTICE_BOT_SKILLS.map((value) => (
              <option value={value} key={value}>{value}-star</option>
            ))}
          </select>
        </label>
      </div>
      <button type="button" disabled={busy || disabled} onClick={onJoin}>
        {busy ? 'Opening table…' : 'Join table'}
      </button>
    </section>
  )
}

export function PracticeQueuePanel({ queue }: { queue: PracticeQueue }) {
  return (
    <section className="practice-bot-panel practice-bot-queue" aria-live="polite">
      <p className="practice-bot-eyebrow">Human-first queue</p>
      <h2>{queue.seats.length} of {queue.requiredPlayers} seats ready</h2>
      <div className="practice-bot-seat-grid">
        {Array.from({ length: queue.requiredPlayers }, (_, index) => queue.seats[index] ?? null)
          .map((seat, index) => (
            <div className={seat ? 'is-filled' : ''} key={seat?.seatId ?? index}>
              <span>{seat?.displayName.slice(0, 1).toUpperCase() ?? '·'}</span>
              <strong>{seat?.displayName ?? 'Open seat'}</strong>
              <small>{seat ? seat.status : `Seat ${index + 1}`}</small>
            </div>
          ))}
      </div>
      <p>Waiting briefly for more people. Empty seats will then be filled automatically.</p>
    </section>
  )
}

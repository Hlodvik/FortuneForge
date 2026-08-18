import { useState } from 'react'
import type { CardRoomActivity } from './cardRoomHistoryTypes'

export function CardRoomHistory({
  activities,
  loading,
  error,
  busyId,
  onSelect,
}: {
  activities: readonly CardRoomActivity[]
  loading: boolean
  error: string | null
  busyId: string | null
  onSelect: (activity: CardRoomActivity) => void
}) {
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const groups = [
    ['blackjack', 'Blackjack'],
    ['texas-holdem', 'Texas Hold’em'],
    ['solitaire', 'Solitaire'],
  ] as const

  return (
    <div className="card-room-history-content">
      <header>
        <small>Your games</small>
        <strong>History</strong>
      </header>

      {loading && <p role="status">Loading games…</p>}
      {!loading && error !== null && <p role="alert">{error}</p>}
      {!loading && error === null && (
        <>
          <div className="card-room-history-games">
            {groups.map(([game, label]) => {
              const gameActivities = activities.filter((activity) => activity.game === game)
              const active = gameActivities.filter((activity) => activity.completedAtUtc === null || activity.unseen)
              const completed = gameActivities.filter((activity) => activity.completedAtUtc !== null && !activity.unseen)
              return <details className="card-room-history-game" key={game}>
                <summary><strong>{label}</strong><span>{gameActivities.length}</span></summary>
                <HistorySection activities={active} busyId={busyId} empty="No active games."
                  expandedId={expandedId} onExpand={setExpandedId} onSelect={onSelect} title="Active" />
                <HistorySection activities={completed} busyId={busyId} empty="No completed games yet."
                  expandedId={expandedId} onExpand={setExpandedId} onSelect={onSelect} title="Completed" />
              </details>
            })}
          </div>
        </>
      )}
    </div>
  )
}

function HistorySection({
  title,
  activities,
  empty,
  busyId,
  onSelect,
  expandedId,
  onExpand,
}: {
  title: string
  activities: readonly CardRoomActivity[]
  empty: string
  busyId: string | null
  onSelect: (activity: CardRoomActivity) => void
  expandedId: string | null
  onExpand: (id: string | null) => void
}) {
  return (
    <section className="card-room-history-section" aria-labelledby={`card-room-history-${title.replace(' ', '-').toLowerCase()}`}>
      <h2 id={`card-room-history-${title.replace(' ', '-').toLowerCase()}`}>{title}</h2>
      {activities.length === 0 ? <p>{empty}</p> : (
        <ol>
          {activities.map((activity) => (
            <li key={activity.id} className={activity.unseen ? 'is-unseen' : ''}>
              <button type="button" disabled={busyId !== null} onClick={() => {
                onExpand(expandedId === activity.id ? null : activity.id)
                onSelect(activity)
              }}>
                <span>
                  <small>{activity.gameLabel}</small>
                  <strong>{activity.title}</strong>
                  <span>{activity.summary}</span>
                </span>
                <span className="card-room-history-section__action">
                  {busyId === activity.id ? 'Opening…' : actionLabel(activity)}
                </span>
              </button>
              {expandedId === activity.id && activity.completedAtUtc !== null && (
                <dl className="card-room-history-stats">
                  <div><dt>{activity.game === 'texas-holdem' ? 'Hands' : activity.game === 'blackjack' ? 'Rounds' : 'Game'}</dt><dd>{activity.rounds ?? 1}</dd></div>
                  <div><dt>Played</dt><dd>{formatDuration(activity.startedAtUtc, activity.completedAtUtc)}</dd></div>
                  <div><dt>Committed</dt><dd>R{(activity.wagerCredits ?? 0).toFixed(2)}</dd></div>
                  <div><dt>Returned</dt><dd>R{(activity.winningsCredits ?? 0).toFixed(2)}</dd></div>
                  <div><dt>Net</dt><dd className={(activity.netCredits ?? 0) >= 0 ? 'is-positive' : 'is-negative'}>
                    {(activity.netCredits ?? 0) < 0 ? '−' : '+'}R{Math.abs(activity.netCredits ?? 0).toFixed(2)}
                  </dd></div>
                </dl>
              )}
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

function formatDuration(startedAtUtc: string, completedAtUtc: string): string {
  const seconds = Math.max(0, Math.round((Date.parse(completedAtUtc) - Date.parse(startedAtUtc)) / 1000))
  const minutes = Math.floor(seconds / 60)
  return `${minutes}m ${seconds % 60}s`
}

function actionLabel(activity: CardRoomActivity): string {
  if (activity.completedAtUtc === null) return 'Resume'
  if (activity.unseen && activity.requiresClaim) return 'Open & claim'
  if (activity.unseen) return 'Open result'
  return 'View'
}

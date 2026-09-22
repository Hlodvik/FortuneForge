import { useEffect, useState } from 'react'
import type { KeyboardEvent } from 'react'
import type {
  ArcadeCompetitionGateway,
  ArcadeCompetitionPeriod,
  ArcadeCompetitionSnapshot,
  ArcadeCompetitionWindow,
} from './arcadeCompetitionApi'
import {
  canNavigateToNextCompetitionWindow,
  competitionHistoryAt,
  formatCompetitionCents,
  formatCompetitionDateTime,
  periodLabel,
  selectCompetitionPeriod,
} from './arcadeCompetitionLeaderboardPresentation'
import type { CompetitionHistoryDirection } from './arcadeCompetitionLeaderboardPresentation'

export type ArcadeCompetitionLeaderboardProps = Readonly<{
  /** Defaults to the currently allowed arcade competition game. */
  gameId?: string
  gateway: ArcadeCompetitionGateway
}>

export function ArcadeCompetitionLeaderboard({
  gameId = 'asteroids',
  gateway,
}: ArcadeCompetitionLeaderboardProps) {
  const [period, setPeriod] = useState<ArcadeCompetitionPeriod>('daily')
  const [at, setAt] = useState<string | undefined>()
  const [snapshot, setSnapshot] = useState<ArcadeCompetitionSnapshot | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [loadAttempt, setLoadAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    setIsLoading(true)
    setError(null)
    setSnapshot(null)
    void gateway.getLeaderboard(gameId, period, at, controller.signal)
      .then((result) => setSnapshot(result))
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(reason instanceof Error ? reason.message : 'Unable to load the arcade competition leaderboard.')
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })
    return () => controller.abort()
  }, [at, gameId, gateway, loadAttempt, period])

  const selectPeriod = (nextPeriod: ArcadeCompetitionPeriod) => {
    const next = selectCompetitionPeriod(nextPeriod)
    setAt(next.at)
    setPeriod(next.period)
  }

  const selectAdjacentPeriod = (event: KeyboardEvent<HTMLButtonElement>, currentPeriod: ArcadeCompetitionPeriod) => {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
    event.preventDefault()
    const periods: readonly ArcadeCompetitionPeriod[] = ['daily', 'weekly', 'all-time']
    const offset = event.key === 'ArrowRight' ? 1 : -1
    const index = (periods.indexOf(currentPeriod) + offset + periods.length) % periods.length
    selectPeriod(periods[index]!)
  }

  const moveWindow = (direction: CompetitionHistoryDirection) => {
    if (snapshot !== null && snapshot.period !== 'all-time') {
      setAt(competitionHistoryAt(snapshot, direction))
    }
  }

  const retry = () => setLoadAttempt((attempt) => attempt + 1)

  return (
    <section aria-label={`${displayGameName(gameId)} leaderboard`} aria-busy={isLoading} className="arcade-leaderboard">
      <h2 className="arcade-leaderboard__heading">{displayGameName(gameId)} leaderboard</h2>
      <div aria-label="Leaderboard period" className="arcade-leaderboard__periods" role="tablist">
        {(['daily', 'weekly', 'all-time'] as const).map((candidate) => (
          <button
            aria-controls="arcade-leaderboard-panel"
            aria-selected={period === candidate}
            className={`arcade-leaderboard__period${period === candidate ? ' arcade-leaderboard__period--active' : ''}`}
            key={candidate}
            onKeyDown={(event) => selectAdjacentPeriod(event, candidate)}
            onClick={() => selectPeriod(candidate)}
            role="tab"
            type="button"
          >
            {periodLabel(candidate)}
          </button>
        ))}
      </div>

      <div aria-live="polite" className="arcade-leaderboard__panel" id="arcade-leaderboard-panel" role="tabpanel">
        {isLoading && <p className="arcade-leaderboard__state arcade-leaderboard__state--loading" role="status">Loading leaderboard…</p>}
        {error !== null && <div className="arcade-leaderboard__error">
          <p className="arcade-leaderboard__state arcade-leaderboard__state--error" role="alert">{error}</p>
          <button className="arcade-leaderboard__retry" onClick={retry} type="button">Retry leaderboard</button>
        </div>}
        {!isLoading && error === null && snapshot !== null && (
          <ArcadeCompetitionLeaderboardView snapshot={snapshot} onNavigate={moveWindow} />
        )}
      </div>
    </section>
  )
}

export function ArcadeCompetitionLeaderboardView({
  snapshot,
  onNavigate,
}: Readonly<{
  snapshot: ArcadeCompetitionSnapshot
  onNavigate?: (direction: CompetitionHistoryDirection) => void
}>) {
  const window = snapshot.period === 'all-time' ? null : snapshot
  return (
    <div className="arcade-leaderboard__results">
      {window !== null && <CompetitionWindowDetails window={window} onNavigate={onNavigate} />}
      {snapshot.leaderboard.length === 0
        ? <p className="arcade-leaderboard__state arcade-leaderboard__state--empty" role="status">No completed scores yet.</p>
        : (
          <ol aria-label="Leaderboard" className="arcade-leaderboard__rows">
            {snapshot.leaderboard.map((placement) => (
              <li className="arcade-leaderboard__row" key={`${placement.position}-${placement.playerId}`}>
                <span className="arcade-leaderboard__position">#{placement.position}</span>
                <span className="arcade-leaderboard__player">{placement.playerId}</span>
                <strong className="arcade-leaderboard__score">{placement.score}</strong>
              </li>
            ))}
          </ol>
        )}
    </div>
  )
}

function CompetitionWindowDetails({
  window,
  onNavigate,
}: Readonly<{
  window: ArcadeCompetitionWindow
  onNavigate?: (direction: CompetitionHistoryDirection) => void
}>) {
  return (
    <div className="arcade-leaderboard__window">
      <p className="arcade-leaderboard__dates">
        <time dateTime={window.startsAtUtc}>{formatCompetitionDateTime(window.startsAtUtc)}</time>
        <span aria-hidden="true"> – </span>
        <time dateTime={window.endsAtUtc}>{formatCompetitionDateTime(window.endsAtUtc)}</time>
      </p>
      <dl className="arcade-leaderboard__summary">
        <div><dt>Jackpot</dt><dd>{window.isCompleted ? 'Final jackpot' : 'Current jackpot'}: {formatCompetitionCents(window.visibleJackpotCents)}</dd></div>
        <div><dt>Entries</dt><dd>{window.entriesOpen ? 'Open' : 'Closed'}</dd></div>
        <div><dt>Players</dt><dd>{window.totalUniquePlayers}</dd></div>
      </dl>
      {window.solePlayerRefund !== null && (
        <p className="arcade-leaderboard__refund">Sole-player refund: {formatCompetitionCents(window.solePlayerRefund.amountCents)} to {window.solePlayerRefund.playerId}</p>
      )}
      {onNavigate !== undefined && (
        <nav aria-label={`${periodLabel(window.period)} leaderboard history`} className="arcade-leaderboard__history">
          <button onClick={() => onNavigate('previous')} type="button">Previous {periodLabel(window.period)}</button>
          {canNavigateToNextCompetitionWindow(window) && <button onClick={() => onNavigate('next')} type="button">Next {periodLabel(window.period)}</button>}
        </nav>
      )}
    </div>
  )
}

function displayGameName(gameId: string): string {
  return gameId === 'asteroids' ? 'Asteroids' : gameId
}

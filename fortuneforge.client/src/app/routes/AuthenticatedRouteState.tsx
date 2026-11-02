import { GameLoadingScreen } from '../../features/game-loading/GameLoadingScreen'
import { useGameLoadingEntry } from '../../features/game-loading/gameLoadingContext'
import { RouteStateLayout } from '../RouteStateLayout'

export function AuthenticatedRouteState({
  error,
  loadingLabel,
  errorTitle,
  onRetry,
}: {
  error: string | null
  loadingLabel: string
  errorTitle: string
  onRetry: () => void
}) {
  const entry = useGameLoadingEntry()
  const pathname = typeof window === 'undefined' ? '' : window.location.pathname.replace(/\/+$/, '')
  const homeHref = pathname.startsWith('/demo') || pathname.endsWith('/demo') ? '/' : '/home'
  if (error === null && entry) return <RouteStateLayout entry={entry}><GameLoadingScreen entry={entry} /></RouteStateLayout>

  return (
    <RouteStateLayout entry={entry}><div className="player-page">
      <main className="player-main">
        {error === null ? (
          <div className="player-state" role="status">{loadingLabel}</div>
        ) : (
          <div className="player-state player-state--error" role="alert">
            <strong>{errorTitle}</strong>
            <span>{error}</span>
            <button className="landing-button landing-button--secondary" type="button" onClick={onRetry}>
              Try again
            </button>
            <a href={homeHref}>Return home</a>
          </div>
        )}
      </main>
    </div></RouteStateLayout>
  )
}

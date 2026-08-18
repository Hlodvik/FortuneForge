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
  return (
    <div className="player-page">
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
            <a href="/home">Return home</a>
          </div>
        )}
      </main>
    </div>
  )
}

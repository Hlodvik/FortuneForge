export function NotFoundPage() {
  return (
    <div className="player-page">
      <main className="player-main">
        <div className="player-state player-state--error" role="alert">
          <strong>Page not found.</strong>
          <span>The Fortune Forge page you requested does not exist.</span>
          <a href="/">Return to the landing page</a>
        </div>
      </main>
    </div>
  )
}

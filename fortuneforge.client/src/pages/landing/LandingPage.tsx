import { useEffect } from 'react'
import { useOptionalAccountSession } from '../../features/account/useOptionalAccountSession'
import '../index.css'

export function LandingPage() {
  const { account, isLoading: isCheckingSession } = useOptionalAccountSession()

  useEffect(() => {
    if (account !== null) {
      window.location.replace('/home')
    }
  }, [account])

  if (account !== null) {
    return (
      <div className="player-page">
        <main className="player-main">
          <div className="player-state" role="status">Opening your home…</div>
        </main>
      </div>
    )
  }

  return (
    <div className="landing-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>

        <nav className="landing-nav" aria-label="Primary navigation">
          <span className="landing-nav__status">
            <span aria-hidden="true">●</span>
            {isCheckingSession ? 'Checking saved session' : 'Public preview'}
          </span>
          {!isCheckingSession && <a className="landing-nav__link" href="/login">Log in</a>}
          <a className="landing-nav__link" href="/slots">Enter game</a>
        </nav>
      </header>

      <main>
        <section className="landing-hero" aria-labelledby="landing-title">
          <div className="landing-hero__copy">
            <p className="landing-eyebrow">
              <span aria-hidden="true">✦</span> Public access is open
            </p>
            <h1 id="landing-title">
              Find your fortune
              <span>your way.</span>
            </h1>
            <p className="landing-hero__lede">
              Step into a jewel-bright collection of slots, card tables, and arcade games built for a
              quick play or a long night.
            </p>

            <div className="landing-hero__actions">
              {!isCheckingSession && (
                <a className="landing-button landing-button--primary" href="/create-account">
                  Create Account <span aria-hidden="true">→</span>
                </a>
              )}
              <a className="landing-button landing-button--secondary" href="/demo">
                Play demo
              </a>
              {!isCheckingSession && <a className="landing-button landing-button--secondary" href="/login">
                Log in
              </a>}
            </div>
            <p className="landing-hero__invite-note">
              <span aria-hidden="true">✦</span>{' '}
              {isCheckingSession
                ? 'Checking this browser for your saved session.'
                : 'Create an account and start playing—no invitation needed.'}
            </p>

          </div>
        </section>

        <section className="landing-invite" aria-labelledby="invite-title">
          <div>
            <p className="landing-invite__eyebrow">
              {isCheckingSession
                ? 'Restoring your session'
                : 'Your seat is waiting'}
            </p>
            <h2 id="invite-title">
              {isCheckingSession
                ? 'One moment…'
                : 'Ready to join?'}
            </h2>
            <p>
              {isCheckingSession
                ? 'Looking for an active Fortune Forge account on this device.'
                : 'Create your player profile and take your place in the Fortune Forge.'}
            </p>
          </div>
          {!isCheckingSession && (
            <a className="landing-button landing-button--gold" href="/create-account">
              Create Account <span aria-hidden="true">→</span>
            </a>
          )}
        </section>
      </main>

      <footer className="landing-footer">
        <span>Fortune Forge</span>
        <span>Public preview · Account creation open</span>
      </footer>
    </div>
  )
}

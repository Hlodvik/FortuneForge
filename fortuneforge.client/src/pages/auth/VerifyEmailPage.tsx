import '@fontsource-variable/sora'
import '../index.css'

export function VerifyEmailPage() {
  return (
    <div className="account-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <svg className="account-brand__spade" viewBox="0 0 48 48" fill="currentColor" stroke="none" aria-hidden="true" focusable="false">
            <path d="M24 2C20 11 5 17 5 27c0 9 10 13 17 5-1 7-5 11-10 13h24c-5-2-9-6-10-13 7 8 17 4 17-5C43 17 28 11 24 2Z" />
          </svg>
          <span>Fortune Forge</span>
        </a>
        <a className="landing-nav__link" href="/login">Log in</a>
      </header>

      <main className="account-main">
        <section className="account-card" aria-labelledby="verify-email-title">
          <div className="account-success" role="status">
            <span className="account-success__icon" aria-hidden="true">✓</span>
            <p className="account-eyebrow">Verification complete</p>
            <h1 id="verify-email-title">Your email is verified.</h1>
            <p>Log in to activate your Fortune Forge account and start playing.</p>
            <a className="landing-button landing-button--primary" href="/login">
              Continue to login <span aria-hidden="true">→</span>
            </a>
          </div>
        </section>
      </main>
    </div>
  )
}

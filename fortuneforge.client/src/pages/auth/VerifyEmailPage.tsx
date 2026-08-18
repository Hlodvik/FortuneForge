import wukongMedallion from '../../assets/slots/symbols/wukong/wukong-medallion.png'
import '../index.css'

export function VerifyEmailPage() {
  return (
    <div className="account-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <a className="landing-nav__link" href="/login">Log in</a>
      </header>

      <main className="account-main">
        <section className="account-card" aria-labelledby="verify-email-title">
          <div className="account-card__symbol" aria-hidden="true">
            <img src={wukongMedallion} alt="" draggable="false" />
          </div>

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

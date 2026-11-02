import '@fontsource-variable/sora'
import { SiteNavbar } from '../../components/SiteNavbar'
import '../index.css'

export function VerifyEmailPage() {
  return (
    <div className="account-page">
      <SiteNavbar mode="public" currentPublicPage="verify-email" />

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

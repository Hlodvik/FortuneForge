import { useState, type FormEvent } from 'react'
import wukongMedallion from '../../assets/slots/symbols/wukong/wukong-medallion.png'
import {
  createAccount,
  resendVerification,
  type CreateAccountInput,
  type CreateAccountResponse,
} from './services/accountsApi'
import './landing.css'

export function CreateAccountPage() {
  const [registration, setRegistration] = useState<CreateAccountResponse | null>(null)
  const [registrationEmail, setRegistrationEmail] = useState('')
  const [registrationCredentials, setRegistrationCredentials] = useState<CreateAccountInput | null>(null)
  const [verificationSent, setVerificationSent] = useState(false)
  const [resendMessage, setResendMessage] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isResending, setIsResending] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)

    const input = {
      playerName: String(form.get('playerName') ?? ''),
      email: String(form.get('email') ?? ''),
      password: String(form.get('password') ?? ''),
    }
    setIsSubmitting(true)
    setSubmitError(null)
    try {
      const createdRegistration = await createAccount(input)
      setRegistration(createdRegistration)
      setRegistrationEmail(input.email)
      setRegistrationCredentials(input)
      setVerificationSent(createdRegistration.verificationEmailSent)
    } catch (error) {
      setSubmitError(error instanceof Error ? error.message : 'Account creation failed. Please try again.')
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handleResend() {
    if (registrationCredentials === null) {
      return
    }

    setIsResending(true)
    setResendMessage(null)
    try {
      const result = await resendVerification(
        registrationCredentials.email,
        registrationCredentials.password,
      )
      setVerificationSent(result.verificationEmailSent || result.emailVerified)
      setResendMessage(result.emailVerified
        ? 'Your email is already verified. Continue to login.'
        : 'A fresh verification email is on its way.')
    } catch (error) {
      setResendMessage(error instanceof Error ? error.message : 'Verification email could not be sent.')
    } finally {
      setIsResending(false)
    }
  }

  return (
    <div className="account-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <a className="landing-nav__link" href="/">Back home</a>
      </header>

      <main className="account-main">
        <section className="account-card" aria-labelledby="account-title">
          <div className="account-card__symbol" aria-hidden="true">
            <img src={wukongMedallion} alt="" draggable="false" />
          </div>

          {registration !== null ? (
            <div className="account-success" role="status">
              <span className="account-success__icon account-success__icon--email" aria-hidden="true">✉</span>
              <p className="account-eyebrow">One last step</p>
              <h1 id="account-title">Check your inbox.</h1>
              <p>
                Welcome, {registration.account.playerName}. Firebase is verifying{' '}
                <strong>{registrationEmail}</strong> before the account can be used.
              </p>
              <p className="account-verification-note">
                {verificationSent
                  ? 'Open the verification link in the email, then return to log in.'
                  : 'The account was created, but the first email could not be sent. Try again below.'}
              </p>
              <div className="account-success__actions">
                <button
                  className="landing-button landing-button--secondary"
                  type="button"
                  disabled={isResending}
                  onClick={handleResend}
                >
                  {isResending ? 'Sending…' : 'Resend email'}
                </button>
                <a className="landing-button landing-button--primary" href="/login">
                  Go to login <span aria-hidden="true">→</span>
                </a>
              </div>
              {resendMessage !== null && <p className="account-verification-message">{resendMessage}</p>}
            </div>
          ) : (
            <>
              <p className="account-eyebrow">Public access</p>
              <h1 id="account-title">Create your account</h1>
              <p className="account-card__intro">
                Choose your player name and enter the Fortune Forge public preview.
              </p>

              <form className="account-form" onSubmit={handleSubmit}>
                <div className="account-form__split">
                  <label>
                    Player name
                    <input name="playerName" autoComplete="nickname" placeholder="LuckyCloud" minLength={3} maxLength={24} required />
                  </label>
                  <label>
                    Email
                    <input name="email" type="email" autoComplete="email" placeholder="you@example.com" required />
                  </label>
                </div>
                <label>
                  Password
                  <input
                    name="password"
                    type="password"
                    autoComplete="new-password"
                    placeholder="At least 8 characters"
                    minLength={8}
                    required
                  />
                </label>
                {submitError !== null && (
                  <p className="account-form__error" role="alert">{submitError}</p>
                )}
                <button
                  className="landing-button landing-button--primary account-form__submit"
                  type="submit"
                  disabled={isSubmitting}
                >
                  {isSubmitting ? 'Creating account…' : 'Create Account'}
                  {!isSubmitting && <span aria-hidden="true">→</span>}
                </button>
              </form>
              <p className="account-card__fine-print">
                New accounts must verify their email before login or gameplay is unlocked.
              </p>
            </>
          )}
        </section>
      </main>
    </div>
  )
}

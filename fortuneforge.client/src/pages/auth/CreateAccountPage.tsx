import { useState, type FormEvent } from 'react'
import '@fontsource-variable/sora'
import {
  createAccount,
  resendVerification,
  type CreateAccountInput,
  type CreateAccountResponse,
} from '../../features/account/services/accountsApi'
import '../index.css'

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
    <div className="account-page account-page--create">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <svg className="account-brand__spade" viewBox="0 0 48 48" fill="currentColor" stroke="none" aria-hidden="true" focusable="false">
            <path d="M24 2C20 11 5 17 5 27c0 9 10 13 17 5-1 7-5 11-10 13h24c-5-2-9-6-10-13 7 8 17 4 17-5C43 17 28 11 24 2Z" />
          </svg>
          <span>Fortune Forge</span>
        </a>
        <a className="landing-nav__link" href="/">Back home</a>
      </header>

      <main className="account-main">
        <section className={'account-card account-card--create' + (registration !== null ? ' is-complete' : '')} aria-labelledby="account-title">
          <span className="account-card__symbol" aria-hidden="true" />

          {registration !== null ? (
            <div className="account-success" role="status">
              <span className="account-success__icon account-success__icon--email" aria-hidden="true">✉</span>
              <p className="account-eyebrow">One last step</p>
              <h1 id="account-title">Check your inbox.</h1>
              <p>
                Welcome, {registration.account.playerName}. Please verify{' '}
                <strong>{registrationEmail}</strong> before logging in.
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
              <div className="account-card__heading">
                <p className="account-eyebrow">Your player profile</p>
                <h1 id="account-title">Create your account</h1>
                <p className="account-card__intro">
                  Join Fortune Forge. Choose a player name and we’ll send a verification link to your email.
                </p>
              </div>
              <div className="account-card__fields">
                <form className="account-form" onSubmit={handleSubmit}>
                  <div className="account-form__split">
                    <label>
                      Player name
                      <input name="playerName" autoComplete="nickname" placeholder="Choose a player name" minLength={3} maxLength={24} required />
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
                    {isSubmitting ? 'Creating account…' : 'Create account'}
                    {!isSubmitting && <span aria-hidden="true">→</span>}
                  </button>
                </form>
                <p className="account-card__fine-print">
                  Verify your email before logging in. Already have an account? <a href="/login">Log in</a>
                </p>
              </div>
            </>
          )}
        </section>
      </main>
    </div>
  )
}

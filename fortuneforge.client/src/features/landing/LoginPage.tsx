import { useEffect, useState, type FormEvent } from 'react'
import wukongMedallion from '../../assets/slots/symbols/wukong/wukong-medallion.png'
import {
  AccountRequestError,
  loginAccount,
  resendVerification,
} from './services/accountsApi'
import { useOptionalAccountSession } from './useOptionalAccountSession'
import './landing.css'

type PendingVerificationCredentials = {
  email: string
  password: string
}

export function LoginPage() {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isResending, setIsResending] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [resendMessage, setResendMessage] = useState<string | null>(null)
  const [pendingVerification, setPendingVerification] = useState<PendingVerificationCredentials | null>(null)
  const requestedDestination = new URLSearchParams(window.location.search).get('returnTo')
  const loginDestination = requestedDestination !== null &&
    requestedDestination.startsWith('/') &&
    !requestedDestination.startsWith('//')
    ? requestedDestination
    : '/home'
  const { account, isLoading: isCheckingSession } = useOptionalAccountSession()

  useEffect(() => {
    if (account !== null) {
      window.location.replace(loginDestination)
    }
  }, [account, loginDestination])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)

    const credentials = {
      email: String(form.get('email') ?? ''),
      password: String(form.get('password') ?? ''),
      remainLoggedIn: form.get('remainLoggedIn') === 'on',
    }

    setIsSubmitting(true)
    setSubmitError(null)
    setResendMessage(null)
    setPendingVerification(null)
    try {
      await loginAccount(credentials)
      window.location.assign(loginDestination)
    } catch (error) {
      setSubmitError(error instanceof Error ? error.message : 'Login failed. Please try again.')
      if (
        error instanceof AccountRequestError &&
        error.status === 403 &&
        error.title === 'Email verification required'
      ) {
        setPendingVerification(credentials)
      }
      setIsSubmitting(false)
    }
  }

  async function handleResend() {
    if (pendingVerification === null) {
      return
    }

    setIsResending(true)
    setResendMessage(null)
    try {
      const result = await resendVerification(
        pendingVerification.email,
        pendingVerification.password,
      )
      setResendMessage(result.emailVerified
        ? 'Your email is verified. Press Log in again to continue.'
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
        <a className="landing-nav__link" href="/create-account">Create account</a>
      </header>

      <main className="account-main">
        <section className="account-card" aria-labelledby="login-title">
          <div className="account-card__symbol" aria-hidden="true">
            <img src={wukongMedallion} alt="" draggable="false" />
          </div>
          <p className="account-eyebrow">Welcome back</p>
          <h1 id="login-title">Log in</h1>
          <p className="account-card__intro">
            Return to your account, check your fortune, and keep playing above the clouds.
          </p>

          <form className="account-form" onSubmit={handleSubmit}>
            <label>
              Email
              <input
                name="email"
                type="email"
                autoComplete="email"
                placeholder="you@example.com"
                required
              />
            </label>
            <label>
              Password
              <input
                name="password"
                type="password"
                autoComplete="current-password"
                placeholder="Your password"
                required
              />
            </label>
            <label className="account-form__remember">
              <input
                name="remainLoggedIn"
                type="checkbox"
                defaultChecked
              />
              <span>
                <strong>Remain logged in</strong>
                <small>Keep this device signed in for up to 30 days.</small>
              </span>
            </label>
            {submitError !== null && (
              <p className="account-form__error" role="alert">{submitError}</p>
            )}
            {pendingVerification !== null && (
              <div className="account-verification-resend">
                <button
                  className="landing-button landing-button--secondary"
                  type="button"
                  disabled={isResending}
                  onClick={handleResend}
                >
                  {isResending ? 'Sending…' : 'Resend verification email'}
                </button>
                {resendMessage !== null && <p>{resendMessage}</p>}
              </div>
            )}
            <button
              className="landing-button landing-button--primary account-form__submit"
              type="submit"
              disabled={isSubmitting || isCheckingSession || account !== null}
            >
              {isCheckingSession || account !== null
                ? 'Checking saved session…'
                : isSubmitting ? 'Logging in…' : 'Log in'}
              {!isSubmitting && !isCheckingSession && account === null && <span aria-hidden="true">→</span>}
            </button>
          </form>
          <p className="account-card__fine-print">
            New to Fortune Forge? <a href="/create-account">Create your account</a>.
          </p>
        </section>
      </main>
    </div>
  )
}

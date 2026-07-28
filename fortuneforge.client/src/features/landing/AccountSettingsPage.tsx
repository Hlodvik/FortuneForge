import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import {
  changeAccountPassword,
  deactivateCurrentAccount,
  updateCurrentAccount,
} from './services/accountsApi'
import { useAuthenticatedAccount } from './useAuthenticatedAccount'
import './landing.css'

type OpenPanel = 'username' | 'password' | 'delete' | null

export function AccountSettingsPage() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount()
  const [openPanel, setOpenPanel] = useState<OpenPanel>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const toastTimerRef = useRef<number | null>(null)

  useEffect(() => () => {
    if (toastTimerRef.current !== null) {
      window.clearTimeout(toastTimerRef.current)
    }
  }, [])

  function showToast(message: string) {
    setToast(message)
    if (toastTimerRef.current !== null) {
      window.clearTimeout(toastTimerRef.current)
    }
    toastTimerRef.current = window.setTimeout(() => setToast(null), 3_000)
  }

  function togglePanel(panel: Exclude<OpenPanel, null>) {
    setActionError(null)
    setOpenPanel((current) => current === panel ? null : panel)
  }

  async function handleUsernameChange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const playerName = String(form.get('playerName') ?? '')

    setIsSubmitting(true)
    setActionError(null)
    try {
      const updatedAccount = await updateCurrentAccount(playerName)
      setOpenPanel(null)
      reload()
      showToast(`Username changed to ${updatedAccount.playerName}.`)
    } catch (requestError) {
      setActionError(requestError instanceof Error ? requestError.message : 'Username update failed.')
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handlePasswordChange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const formElement = event.currentTarget
    const form = new FormData(formElement)
    const newPassword = String(form.get('newPassword') ?? '')
    const confirmedPassword = String(form.get('confirmedPassword') ?? '')
    if (newPassword !== confirmedPassword) {
      setActionError('The new passwords do not match.')
      return
    }

    setIsSubmitting(true)
    setActionError(null)
    try {
      await changeAccountPassword(String(form.get('currentPassword') ?? ''), newPassword)
      formElement.reset()
      setOpenPanel(null)
      showToast('Password updated successfully.')
    } catch (requestError) {
      setActionError(requestError instanceof Error ? requestError.message : 'Password update failed.')
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handleDelete(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setIsSubmitting(true)
    setActionError(null)
    try {
      await deactivateCurrentAccount(String(form.get('deletePassword') ?? ''))
      window.location.replace('/')
    } catch (requestError) {
      setActionError(requestError instanceof Error ? requestError.message : 'Account deactivation failed.')
      setIsSubmitting(false)
    }
  }

  return (
    <div className="player-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <div className="landing-bar__account">
          {account !== null && <ForgeCreditAmount amount={account.balances.slotsCredits} />}
          <a className="landing-nav__link" href="/home">Back home</a>
        </div>
      </header>

      <main className="settings-main">
        {isLoading && <div className="player-state" role="status">Opening settings…</div>}
        {!isLoading && error !== null && (
          <div className="player-state player-state--error" role="alert">
            <span>{error}</span>
            <button className="landing-button landing-button--secondary" type="button" onClick={reload}>Try again</button>
          </div>
        )}
        {!isLoading && account !== null && (
          <section className="settings-card" aria-labelledby="settings-title">
            <header className="settings-card__header">
              <p className="account-eyebrow">Account settings</p>
              <h1 id="settings-title">{account.playerName}</h1>
              <p>{account.email}</p>
            </header>

            <div className="settings-actions">
              <button type="button" onClick={() => togglePanel('username')}>
                <span className="settings-action__icon" aria-hidden="true">✎</span>
                <span><strong>Change username</strong><small>Choose a unique player name</small></span>
              </button>
              <button type="button" onClick={() => togglePanel('password')}>
                <span className="settings-action__icon" aria-hidden="true">✦</span>
                <span><strong>Change password</strong><small>Update your account password</small></span>
              </button>
              <a href="/home/history">
                <span className="settings-action__icon" aria-hidden="true">↗</span>
                <span><strong>User history</strong><small>Review your tracked slot results</small></span>
              </a>
              <button className="settings-action--danger" type="button" onClick={() => togglePanel('delete')}>
                <span className="settings-action__icon" aria-hidden="true">×</span>
                <span><strong>Deactivate account</strong><small>Disable sign-in while retaining your records</small></span>
              </button>
            </div>

            {openPanel === 'username' && (
              <form className="settings-panel account-form" onSubmit={handleUsernameChange}>
                <h2>Change username</h2>
                <p>Usernames are unique and are not case-sensitive.</p>
                <label>
                  New username
                  <input
                    name="playerName"
                    type="text"
                    autoComplete="nickname"
                    defaultValue={account.playerName}
                    minLength={3}
                    maxLength={24}
                    required
                  />
                </label>
                <small className="settings-panel__hint">Use 3–24 letters, numbers, spaces, underscores, or hyphens.</small>
                {actionError !== null && <p className="account-form__error" role="alert">{actionError}</p>}
                <button className="landing-button landing-button--primary" type="submit" disabled={isSubmitting}>
                  {isSubmitting ? 'Checking availability…' : 'Save username'}
                </button>
              </form>
            )}

            {openPanel === 'password' && (
              <form className="settings-panel account-form" onSubmit={handlePasswordChange}>
                <h2>Change password</h2>
                <label>Current password<input name="currentPassword" type="password" autoComplete="current-password" required /></label>
                <div className="account-form__split">
                  <label>New password<input name="newPassword" type="password" autoComplete="new-password" minLength={8} required /></label>
                  <label>Confirm new password<input name="confirmedPassword" type="password" autoComplete="new-password" minLength={8} required /></label>
                </div>
                {actionError !== null && <p className="account-form__error" role="alert">{actionError}</p>}
                <button className="landing-button landing-button--primary" type="submit" disabled={isSubmitting}>
                  {isSubmitting ? 'Updating…' : 'Update password'}
                </button>
              </form>
            )}

            {openPanel === 'delete' && (
              <form className="settings-panel settings-panel--danger account-form" onSubmit={handleDelete}>
                <h2>Deactivate account</h2>
                <p>This disables sign-in and revokes your sessions. Your profile, game history, invoices, and financial records remain stored.</p>
                <label>Enter your password to confirm<input name="deletePassword" type="password" autoComplete="current-password" required /></label>
                {actionError !== null && <p className="account-form__error" role="alert">{actionError}</p>}
                <button className="landing-button settings-delete-button" type="submit" disabled={isSubmitting}>
                  {isSubmitting ? 'Deactivating…' : 'Deactivate account'}
                </button>
              </form>
            )}
          </section>
        )}
      </main>

      {toast !== null && <div className="account-toast" role="status">{toast}</div>}
    </div>
  )
}

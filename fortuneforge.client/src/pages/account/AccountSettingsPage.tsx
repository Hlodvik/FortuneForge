import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  deactivateCurrentAccount,
  updateCurrentAccount,
  type AccountSummary,
} from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AccountLobbyShell } from './AccountLobbyShell'
import '../index.css'
import './accountSettings.css'

type OpenPanel = 'username' | 'delete' | null

export function AccountSettingsPage() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount()

  return (
    <AccountLobbyShell account={account} activeNav="account">
      {isLoading && <div className="account-settings"><div className="home-lobby__notice" role="status">Opening settings…</div></div>}
      {!isLoading && error !== null && (
        <div className="account-settings"><div className="home-lobby__notice" role="alert">
          <span>{error}</span>
          <button className="home-lobby__gold-button" type="button" onClick={reload}>Try again</button>
        </div></div>
      )}
      {!isLoading && account !== null && <AccountSettingsContent account={account} reload={reload} />}
    </AccountLobbyShell>
  )
}

export function AccountSettingsContent({ account, reload }: { account: AccountSummary; reload: () => void }) {
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
    <>
      <div className="account-settings">
          <section aria-labelledby="settings-title">
            <header className="account-settings__heading">
              <p className="account-settings__eyebrow">Your account</p>
              <h1 id="settings-title">Settings</h1>
              <p>Manage your profile and sign-in details.</p>
            </header>

            <div className="account-settings__profile" role="group" aria-label="Your profile">
              <span className="account-settings__profile-avatar" aria-hidden="true">{account.playerName.slice(0, 1).toLocaleUpperCase()}</span>
              <div><span className="account-settings__profile-label">Player profile</span><strong>{account.playerName}</strong><small>{account.email}</small></div>
            </div>

            <div className="account-settings__actions">
              <button type="button" onClick={() => togglePanel('username')} aria-expanded={openPanel === 'username'} aria-controls={openPanel === 'username' ? 'settings-username-panel' : undefined}>
                <span className="account-settings__action-icon" aria-hidden="true">✎</span>
                <span><strong>Change username</strong><small>Choose a unique player name</small></span><span className="account-settings__chevron" aria-hidden="true">›</span>
              </button>
              <a href="/home/change-password">
                <span className="account-settings__action-icon" aria-hidden="true">✦</span>
                <span><strong>Change password</strong><small>Update your account password</small></span><span className="account-settings__chevron" aria-hidden="true">›</span>
              </a>
              <a href="/home/history">
                <span className="account-settings__action-icon" aria-hidden="true">↗</span>
                <span><strong>Game history</strong><small>Review your games and account activity</small></span><span className="account-settings__chevron" aria-hidden="true">›</span>
              </a>
              <button className="account-settings__action--danger" type="button" onClick={() => togglePanel('delete')} aria-expanded={openPanel === 'delete'} aria-controls={openPanel === 'delete' ? 'settings-delete-panel' : undefined}>
                <span className="account-settings__action-icon" aria-hidden="true">×</span>
                <span><strong>Deactivate account</strong><small>Disable sign-in while retaining your records</small></span><span className="account-settings__chevron" aria-hidden="true">›</span>
              </button>
            </div>

            {openPanel === 'username' && (
              <form id="settings-username-panel" className="account-settings__panel" onSubmit={handleUsernameChange}>
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
                <small className="account-settings__hint">Use 3–24 letters, numbers, spaces, underscores, or hyphens.</small>
                {actionError !== null && <p className="account-settings__form-error" role="alert">{actionError}</p>}
                <button className="account-settings__submit" type="submit" disabled={isSubmitting}>
                  {isSubmitting ? 'Checking availability…' : 'Save username'}
                </button>
              </form>
            )}

            {openPanel === 'delete' && (
              <form id="settings-delete-panel" className="account-settings__panel account-settings__panel--danger" onSubmit={handleDelete}>
                <h2>Deactivate account</h2>
                <p>This disables sign-in and revokes your sessions. Your profile, game history, invoices, and financial records remain stored.</p>
                <label>Enter your password to confirm<input name="deletePassword" type="password" autoComplete="current-password" required /></label>
                {actionError !== null && <p className="account-settings__form-error" role="alert">{actionError}</p>}
                <button className="account-settings__submit account-settings__submit--danger" type="submit" disabled={isSubmitting}>
                  {isSubmitting ? 'Deactivating…' : 'Deactivate account'}
                </button>
              </form>
            )}
          </section>
      </div>

      {toast !== null && <div className="account-settings__toast" role="status">{toast}</div>}
    </>
  )
}

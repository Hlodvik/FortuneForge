import { useState, type FormEvent } from 'react'
import { PasswordRequirements } from '../../features/account/PasswordRequirements'
import { isValidNewPassword } from '../../features/account/passwordPolicy'
import { changeAccountPassword } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AccountLobbyShell } from './AccountLobbyShell'
import '../index.css'
import './accountSettings.css'

export function AccountPasswordPage() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/home/change-password')
  const [actionError, setActionError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isComplete, setIsComplete] = useState(false)
  const [newPassword, setNewPassword] = useState('')
  const [confirmedPassword, setConfirmedPassword] = useState('')
  const passwordsMatch = confirmedPassword.length > 0 && newPassword === confirmedPassword

  async function handlePasswordChange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const formElement = event.currentTarget
    const form = new FormData(formElement)
    const newPassword = String(form.get('newPassword') ?? '')
    const confirmedPassword = String(form.get('confirmedPassword') ?? '')
    if (!isValidNewPassword(newPassword)) {
      setActionError('Choose a new password that meets all four requirements.')
      return
    }
    if (newPassword !== confirmedPassword) {
      setActionError('The new passwords do not match.')
      return
    }

    setIsSubmitting(true)
    setActionError(null)
    try {
      await changeAccountPassword(String(form.get('currentPassword') ?? ''), newPassword)
      formElement.reset()
      setNewPassword('')
      setConfirmedPassword('')
      setIsComplete(true)
    } catch (requestError) {
      setActionError(requestError instanceof Error ? requestError.message : 'Password update failed.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <AccountLobbyShell account={account} activeNav="account">
      <div className="account-settings account-settings--password">
        {isLoading && <div className="home-lobby__notice" role="status">Opening password settings…</div>}
        {!isLoading && error !== null && (
          <div className="home-lobby__notice" role="alert">
            <span>{error}</span>
            <button className="home-lobby__gold-button" type="button" onClick={reload}>Try again</button>
          </div>
        )}
        {!isLoading && account !== null && (
          <>
            <header className="account-settings__heading">
              <p className="account-settings__eyebrow">Your account</p>
              <h1>Change password</h1>
              <p>Keep your sign-in details up to date.</p>
            </header>
            {isComplete ? (
              <div className="account-settings__panel" role="status">
                <h2>Password updated</h2>
                <p>Your new password is ready to use.</p>
                <a className="home-lobby__gold-button" href="/home/settings">Back to account</a>
              </div>
            ) : (
              <form className="account-settings__panel" onSubmit={handlePasswordChange}>
                <label>Current password<input name="currentPassword" type="password" autoComplete="current-password" required /></label>
                <div className="account-settings__form-split">
                  <div className="account-settings__password-field">
                    <label>New password<input name="newPassword" type="password" autoComplete="new-password" minLength={8} maxLength={128} value={newPassword} onChange={(event) => setNewPassword(event.target.value)} aria-describedby="change-password-requirements" required /></label>
                    <PasswordRequirements password={newPassword} id="change-password-requirements" />
                  </div>
                  <label>Confirm new password<input name="confirmedPassword" type="password" autoComplete="new-password" minLength={8} maxLength={128} value={confirmedPassword} onChange={(event) => setConfirmedPassword(event.target.value)} aria-invalid={confirmedPassword.length > 0 && !passwordsMatch} required /></label>
                </div>
                {confirmedPassword.length > 0 && <p className={`password-match${passwordsMatch ? ' is-met' : ''}`} role="status">{passwordsMatch ? '✓ Passwords match' : 'Passwords do not match yet'}</p>}
                {actionError !== null && <p className="account-settings__form-error" role="alert">{actionError}</p>}
                <button className="account-settings__submit" type="submit" disabled={isSubmitting || !isValidNewPassword(newPassword) || !passwordsMatch}>
                  {isSubmitting ? 'Updating…' : 'Update password'}
                </button>
                <a className="account-settings__back-link" href="/home/settings">Back to account</a>
              </form>
            )}
          </>
        )}
      </div>
    </AccountLobbyShell>
  )
}

import { useState } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { logoutAccount } from '../features/account/services/accountsApi'
import { ForgeCreditAmount } from './ForgeCreditAmount'

export function PlayerHeader({ account }: { account: AccountSummary }) {
  const [loggingOut, setLoggingOut] = useState(false)

  async function logout() {
    setLoggingOut(true)
    try { await logoutAccount() }
    finally { window.location.replace('/') }
  }

  return (
    <header className="player-shell-header">
      <a className="player-shell-header__brand" href="/home" aria-label="Fortune Forge home">
        <span className="player-shell-header__spark" aria-hidden="true">✦</span><strong>Fortune Forge</strong>
      </a>
      <div className="player-shell-header__menus">
        <details className="player-shell-menu player-shell-menu--balance">
          <summary><ForgeCreditAmount amount={account.balances.slotsCredits} /></summary>
          <nav aria-label="Balance actions">
            <a href="/home/rand"><strong>Add Rand</strong><small>Recharge your balance</small></a>
            <a href="/home/rand#withdrawal-request"><strong>Request withdrawal</strong><small>Send Rand to your saved bank</small></a>
            <a href={account.role.toLowerCase() === 'admin' ? '/admin/invoices' : '/home/invoices'}>
              <strong>{account.role.toLowerCase() === 'admin' ? 'Customer invoices' : 'Invoices'}</strong>
              <small>View payment records</small>
            </a>
          </nav>
        </details>
        <details className="player-shell-menu player-shell-menu--more">
          <summary aria-label="Account menu"><span></span><span></span><span></span></summary>
          <nav aria-label="Account menu">
            <a href="/home/rand"><strong>Recharge</strong><small>Add Rand to play</small></a>
            <a href="/home/settings"><strong>Account settings</strong><small>Manage your account</small></a>
            <a href="/home/history"><strong>History</strong><small>Review account activity</small></a>
            <button type="button" disabled={loggingOut} onClick={() => void logout()}>
              <strong>{loggingOut ? 'Leaving…' : 'Log out'}</strong><small>End this session</small>
            </button>
          </nav>
        </details>
      </div>
    </header>
  )
}

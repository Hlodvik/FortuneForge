import { useEffect, useRef, useState } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { logoutAccount } from '../features/account/services/accountsApi'
import { HeaderBalance } from './HeaderBalance'

export function PlayerAccountMenus({ account, showBalance = true }: { account: AccountSummary; showBalance?: boolean }) {
  const [loggingOut, setLoggingOut] = useState(false)
  const menuRef = useRef<HTMLDetailsElement>(null)
  const triggerRef = useRef<HTMLElement>(null)

  useEffect(() => {
    const dismissOutside = (event: Event) => {
      const menu = menuRef.current
      if (menu?.open && event.target instanceof Node && !menu.contains(event.target)) menu.open = false
    }
    document.addEventListener('pointerdown', dismissOutside, true)
    document.addEventListener('focusin', dismissOutside)
    return () => {
      document.removeEventListener('pointerdown', dismissOutside, true)
      document.removeEventListener('focusin', dismissOutside)
    }
  }, [])

  async function logout() {
    setLoggingOut(true)
    try { await logoutAccount() }
    finally { window.location.replace('/') }
  }

  return (
      <div className="player-shell-header__menus">
        {showBalance && <HeaderBalance amount={account.balances.slotsCredits} href="/balance" />}
        <details ref={menuRef} className="player-shell-menu player-shell-menu--more"
          onKeyDown={event => {
            if (event.key !== 'Escape' || !event.currentTarget.open) return
            event.preventDefault()
            event.stopPropagation()
            event.currentTarget.open = false
            triggerRef.current?.focus({ preventScroll: true })
          }}>
          <summary ref={triggerRef} aria-label="Account menu"><span></span><span></span><span></span></summary>
          <nav aria-label="Account menu">
            {account.role.toLowerCase() === 'admin' && <a href="/admin"><strong>Admin portal</strong><small>Players, games and operations</small></a>}
            <a href="/balance"><strong>Recharge</strong><small>Add Rand to play</small></a>
            <a href="/balance#withdrawal-request"><strong>Request withdrawal</strong><small>Send Rand to your saved bank</small></a>
            <a href={account.role.toLowerCase() === 'admin' ? '/admin/invoices' : '/home/invoices'}>
              <strong>{account.role.toLowerCase() === 'admin' ? 'Customer invoices' : 'Invoices'}</strong>
              <small>View payment records</small>
            </a>
            <a href="/home/settings"><strong>Account settings</strong><small>Manage your account</small></a>
            <a href="/home/history"><strong>History</strong><small>Review account activity</small></a>
            <button type="button" disabled={loggingOut} onClick={() => void logout()}>
              <strong>{loggingOut ? 'Leaving…' : 'Log out'}</strong><small>End this session</small>
            </button>
          </nav>
        </details>
      </div>
  )
}

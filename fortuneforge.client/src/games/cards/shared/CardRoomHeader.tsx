import { useState, type ReactNode, type SyntheticEvent } from 'react'
import { ForgeCreditAmount } from '../../../components/ForgeCreditAmount'
import { logoutAccount } from '../../../features/account/services/accountsApi'
import './cardRoomHeader.css'

export function CardRoomHeader({
  playerName,
  balanceCredits,
  unseenCount,
  historyContent,
  showOtherGames = true,
  onHistoryToggle,
}: {
  playerName: string
  balanceCredits: number
  unseenCount: number
  historyContent: ReactNode
  showOtherGames?: boolean
  onHistoryToggle?: (open: boolean) => void
}) {
  const [loggingOut, setLoggingOut] = useState(false)
  const handleToggle = (event: SyntheticEvent<HTMLDetailsElement>) => {
    onHistoryToggle?.(event.currentTarget.open)
  }
  const logout = async () => {
    setLoggingOut(true)
    try { await logoutAccount() }
    finally { window.location.replace('/') }
  }

  return (
    <header className="card-room-header">
      <nav className="card-room-header__navigation" aria-label="Card room navigation">
        <a className="card-room-header__brand" href="/home" aria-label="Fortune Forge home">
          <span className="card-room-header__spark" aria-hidden="true">✦</span><strong>Fortune Forge</strong>
        </a>
        {showOtherGames && <a className="card-room-header__other-games" href="/games">Other games</a>}
        <details className="card-room-history" onToggle={handleToggle}>
          <summary className="card-room-header__icon" aria-label={`Game history${unseenCount > 0 ? `, ${unseenCount} new` : ''}`} title="Game history">
            <HistoryIcon />
            {unseenCount > 0 && <span className="card-room-header__badge">{unseenCount > 9 ? '9+' : unseenCount}</span>}
          </summary>
          <section className="card-room-history__panel" aria-label="Game history">
            {historyContent}
          </section>
        </details>
      </nav>

      <div className="card-room-header__menus">
        <details className="card-room-account-menu">
          <summary className="card-room-header__account">
            <strong>{playerName}</strong><ForgeCreditAmount amount={balanceCredits} />
          </summary>
          <nav><a href="/home/rand">Add Rand</a><a href="/home/rand#withdrawal-request">Request withdrawal</a><a href="/home/invoices">Invoices</a></nav>
        </details>
        <details className="card-room-more-menu">
          <summary aria-label="Account menu"><span></span><span></span><span></span></summary>
          <nav><a href="/home/rand">Recharge</a><a href="/home/settings">Settings</a>
            <button type="button" disabled={loggingOut} onClick={() => void logout()}>{loggingOut ? 'Leaving…' : 'Log out'}</button></nav>
        </details>
      </div>
    </header>
  )
}

function HistoryIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path d="M4.5 8.5A8.5 8.5 0 1 1 3.7 16" />
      <path d="M4.5 3.5v5h5M12 7.5V12l3 2" />
    </svg>
  )
}

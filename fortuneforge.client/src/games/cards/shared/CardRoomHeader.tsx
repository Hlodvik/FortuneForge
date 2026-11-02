import { type ReactNode, type SyntheticEvent } from 'react'
import { SiteNavbar } from '../../../components/SiteNavbar'
import { GameSupportControls } from '../../../components/GameSupportControls'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import './cardRoomHeader.css'

export function CardRoomHeader({
  account,
  unseenCount,
  historyContent,
  onHistoryToggle,
}: {
  account: AccountSummary
  unseenCount: number
  historyContent: ReactNode
  onHistoryToggle?: (open: boolean) => void
}) {
  const handleToggle = (event: SyntheticEvent<HTMLDetailsElement>) => {
    onHistoryToggle?.(event.currentTarget.open)
  }
  return (
    <>
      <SiteNavbar mode="game" account={account} />
      <nav className="game-local-controls card-room-local-controls" aria-label="Card room controls">
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
      <GameSupportControls account={account} />
    </>
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

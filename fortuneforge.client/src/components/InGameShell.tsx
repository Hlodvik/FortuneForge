import type { ReactNode } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { InGameNavbar } from './InGameNavbar'
import { GameSupportControls } from './GameSupportControls'
import './InGameShell.css'

type InGameShellProps = Readonly<{
  account?: AccountSummary
  authenticated?: boolean
  title?: string
  reportTitle?: string
  supportControls?: ReactNode
  balance?: number
  onNavigate?: () => void
  children: ReactNode
  className?: string
  bodyClassName?: string
}>

export function InGameShell({ account, authenticated, title, reportTitle, supportControls, balance, onNavigate, children, className, bodyClassName }: InGameShellProps) {
  return <div className="in-game-shell">
    <InGameNavbar account={account} authenticated={authenticated} title={title} balance={balance} onNavigate={onNavigate} />
    <div className={['in-game-shell__surface', className].filter(Boolean).join(' ')} data-game-surface>
      <div className={['in-game-shell__body', bodyClassName].filter(Boolean).join(' ')}>
        {children}
        <GameSupportControls account={account} gameTitle={reportTitle || title}>{supportControls}</GameSupportControls>
      </div>
    </div>
  </div>
}

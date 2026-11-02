import type { ReactNode } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { InGameNavbar, type InGameNavbarProps } from './InGameNavbar'
import { GameSupportControls } from './GameSupportControls'
import './InGameShell.css'

type InGameShellProps = Readonly<{
  account?: AccountSummary
  authenticated?: boolean
  title?: string
  reportTitle?: string
  supportControls?: ReactNode
  theme?: InGameNavbarProps['theme']
  actions?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
}>

export function InGameShell({ account, authenticated, title, reportTitle, supportControls, theme, actions, children, className, bodyClassName }: InGameShellProps) {
  return <div className={['in-game-shell', className].filter(Boolean).join(' ')}>
    <InGameNavbar account={account} authenticated={authenticated} title={title} theme={theme} actions={actions} />
    <div className={['in-game-shell__body', bodyClassName].filter(Boolean).join(' ')}>
      {children}
      <GameSupportControls account={account} gameTitle={reportTitle || title}>{supportControls}</GameSupportControls>
    </div>
  </div>
}

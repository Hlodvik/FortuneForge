import type { ReactNode } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { InGameNavbar, type InGameNavbarProps } from './InGameNavbar'
import './InGameShell.css'

type InGameShellProps = Readonly<{
  account?: AccountSummary
  title?: string
  theme?: InGameNavbarProps['theme']
  actions?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
}>

export function InGameShell({ account, title, theme, actions, children, className, bodyClassName }: InGameShellProps) {
  return <div className={['in-game-shell', className].filter(Boolean).join(' ')}>
    <InGameNavbar account={account} title={title} theme={theme} actions={actions} />
    <div className={['in-game-shell__body', bodyClassName].filter(Boolean).join(' ')}>{children}</div>
  </div>
}

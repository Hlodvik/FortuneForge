import type { ReactNode } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { SiteNavbar } from './SiteNavbar'

export type InGameNavbarProps = Readonly<{
  account?: AccountSummary
  authenticated?: boolean
  title?: string
  reportTitle?: string
  theme?: 'arcade' | 'casino' | 'cards' | 'slots'
  actions?: ReactNode
  className?: string
  balance?: number
}>

/** Compatibility entrypoint for games; site chrome is rendered by SiteNavbar. */
export function InGameNavbar({ account, authenticated = false, title, reportTitle, theme = 'arcade', actions, className, balance }: InGameNavbarProps) {
  return <SiteNavbar mode="game" account={account} authenticated={authenticated} title={title} reportTitle={reportTitle} theme={theme} actions={actions} className={className} balance={balance} />
}

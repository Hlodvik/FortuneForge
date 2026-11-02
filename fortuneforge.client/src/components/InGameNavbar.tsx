import type { AccountSummary } from '../features/account/services/accountsApi'
import { SiteNavbar } from './SiteNavbar'

export type InGameNavbarProps = Readonly<{
  account?: AccountSummary
  authenticated?: boolean
  title?: string
  reportTitle?: string
  balance?: number
  onNavigate?: () => void
}>

/** Compatibility entrypoint for games; site chrome is rendered by SiteNavbar. */
export function InGameNavbar({ account, authenticated = false, title, reportTitle, balance, onNavigate }: InGameNavbarProps) {
  return <SiteNavbar mode="game" account={account} authenticated={authenticated} title={title} reportTitle={reportTitle} balance={balance} onNavigate={onNavigate} />
}

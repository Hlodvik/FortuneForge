import type { AccountSummary } from '../features/account/services/accountsApi'
import { SiteNavbar } from './SiteNavbar'

export { PlayerAccountMenus } from './PlayerAccountMenus'

/** Compatibility entrypoint for legacy account pages. */
export function PlayerHeader({ account }: { account: AccountSummary }) {
  return <SiteNavbar mode="account" account={account} />
}

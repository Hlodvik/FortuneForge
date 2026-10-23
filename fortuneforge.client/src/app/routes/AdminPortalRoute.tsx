import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AdminPortalPage } from '../../pages/admin/portal/AdminPortalPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AdminPortalRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/admin')
  if (isLoading || error || !account) return <AuthenticatedRouteState error={error} loadingLabel="Opening the admin portal…" errorTitle="The admin portal could not be opened." onRetry={reload} />
  if (account.role.toLowerCase() !== 'admin') return <main className="route-state route-state--error" role="alert"><strong>Administrator access required.</strong><a href="/home">Return home</a></main>
  return <AdminPortalPage key={account.userId} playerName={account.playerName} />
}

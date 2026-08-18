import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AdminOperationsPage } from '../../pages/admin/operations/AdminOperationsPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AdminOperationsRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/admin/operations')
  if (isLoading || error !== null || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening operations…" errorTitle="Operations could not be opened." onRetry={reload} />
  }
  if (account.role.toLowerCase() !== 'admin') {
    return <main className="route-state route-state--error" role="alert"><strong>Administrator access required.</strong><a href="/home">Return home</a></main>
  }
  return <AdminOperationsPage />
}

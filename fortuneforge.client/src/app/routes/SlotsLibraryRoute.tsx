import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { SlotsLibraryPage } from '../../pages/slots/SlotsLibraryPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedSlotsLibraryRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/slots')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening the slot collection…" errorTitle="The slot collection could not be opened." onRetry={reload} />
  }
  return <SlotsLibraryPage account={account} />
}

import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { ReleasedSolitaireSession } from './SolitaireRoute'

export function AuthenticatedFreeSolitaireRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/free-solitaire')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Solitaire…" errorTitle="Solitaire could not be opened." onRetry={reload} />
  }
  return <ReleasedSolitaireSession key={account.userId} account={account} freeOnly />
}

import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { FreeSolitairePage } from '../../pages/cards/solitaire/CompetitiveSolitairePage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedFreeSolitaireRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/free-solitaire')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Solitaire…" errorTitle="Solitaire could not be opened." onRetry={reload} />
  }
  return <FreeSolitairePage account={account} />
}

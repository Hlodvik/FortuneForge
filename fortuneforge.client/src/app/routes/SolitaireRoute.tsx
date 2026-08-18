import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { CompetitiveSolitairePage } from '../../pages/cards/solitaire/CompetitiveSolitairePage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedSolitaireRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/solitaire')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening competitive Solitaire…" errorTitle="Competitive Solitaire could not be opened." onRetry={reload} />
  }
  return <CompetitiveSolitairePage account={account} />
}

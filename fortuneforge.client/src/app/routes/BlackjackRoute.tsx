import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { BlackjackTablePage } from '../../pages/cards/blackjack/BlackjackTablePage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedBlackjackRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/blackjack')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening the Blackjack table…" errorTitle="The Blackjack table could not be opened." onRetry={reload} />
  }
  return <BlackjackTablePage account={account} />
}

import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { CreditTexasHoldemPage } from '../../pages/cards/texasHoldem/CreditTexasHoldemPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedTexasHoldemRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/texas-holdem')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening the credit Hold’em table…" errorTitle="The credit Hold’em table could not be opened." onRetry={reload} />
  }
  return <CreditTexasHoldemPage account={account} />
}

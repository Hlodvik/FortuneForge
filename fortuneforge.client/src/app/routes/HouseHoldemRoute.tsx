import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { HouseHoldemPage } from '../../pages/cards/houseHoldem/HouseHoldemPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedHouseHoldemRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/house-holdem')
  if (isLoading || (account === null && error === null)) {
    return <AuthenticatedRouteState error={null} loadingLabel="Opening House Hold’em…" errorTitle="The House Hold’em table could not be opened." onRetry={reload} />
  }
  if (account === null) return <AuthenticatedRouteState error={error} loadingLabel="" errorTitle="The House Hold’em table could not be opened." onRetry={reload} />
  return <HouseHoldemPage account={account} />
}

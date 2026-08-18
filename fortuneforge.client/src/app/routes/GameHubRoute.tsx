import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { OtherGamesPage } from '../../pages/games/OtherGamesPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedGameHubRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening the game rooms…"
      errorTitle="The game rooms could not be opened." onRetry={reload} />
  }
  return <OtherGamesPage account={account} />
}

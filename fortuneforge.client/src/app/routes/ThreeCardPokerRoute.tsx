import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { ThreeCardPokerPage } from '../../pages/cards/threeCardPoker/ThreeCardPokerPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedThreeCardPokerRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/three-card-poker')
  if (isLoading || (account === null && error === null)) {
    return <AuthenticatedRouteState error={null} loadingLabel="Opening Three Card Poker…" errorTitle="The Three Card Poker table could not be opened." onRetry={reload} />
  }
  if (account === null) return <AuthenticatedRouteState error={error} loadingLabel="" errorTitle="The Three Card Poker table could not be opened." onRetry={reload} />
  return <ThreeCardPokerPage account={account} />
}

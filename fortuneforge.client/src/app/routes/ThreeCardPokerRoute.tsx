import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { InGameShell } from '../../components/InGameShell'
import { GameLoadingScreen } from '../../features/game-loading/GameLoadingScreen'
import { resolveGameLoadingEntry } from '../../features/game-loading/gameLoadingRegistry'
import { ThreeCardPokerPage } from '../../pages/cards/threeCardPoker/ThreeCardPokerPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedThreeCardPokerRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/three-card-poker')
  if (isLoading || (account === null && error === null)) {
    return <InGameShell authenticated title="Three Card Poker" theme="cards" className="blackjack-game-shell three-card-poker-shell" bodyClassName="blackjack-shell-body">
      <GameLoadingScreen entry={resolveGameLoadingEntry('/cards/three-card-poker')} statusLabel="Loading" />
    </InGameShell>
  }
  if (account === null) return <AuthenticatedRouteState error={error} loadingLabel="" errorTitle="The Three Card Poker table could not be opened." onRetry={reload} />
  return <ThreeCardPokerPage account={account} />
}

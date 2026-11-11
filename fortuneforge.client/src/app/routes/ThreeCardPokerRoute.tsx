import { ThreeCardPokerBrowser } from '@fortuneforge/games-three-card-poker'
import bundledStyles from '@fortuneforge/games-three-card-poker/styles.css?inline'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { InGameShell } from '../../components/InGameShell'
import { GameLoadingScreen } from '../../features/game-loading/GameLoadingScreen'
import { resolveGameLoadingEntry } from '../../features/game-loading/gameLoadingRegistry'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { ThreeCardPokerPage } from '../../pages/cards/threeCardPoker/ThreeCardPokerPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedThreeCardPokerRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/three-card-poker')
  if (isLoading || (account === null && error === null)) {
    return <InGameShell authenticated title="Three Card Poker" className="blackjack-game-shell three-card-poker-shell" bodyClassName="blackjack-shell-body">
      <GameLoadingScreen entry={resolveGameLoadingEntry('/cards/three-card-poker')} statusLabel="Loading" />
    </InGameShell>
  }
  if (account === null) return <AuthenticatedRouteState error={error} loadingLabel="" errorTitle="The Three Card Poker table could not be opened." onRetry={reload} />
  return <ThreeCardPokerSession key={account.userId} account={account} />
}

function ThreeCardPokerSession({ account }: { account: AccountSummary }) {
  const { Game, pending } = useReleasedGame('three-card-poker', 'Three Card Poker', ThreeCardPokerBrowser, bundledStyles)
  if (!Game) return pending
  return <Game renderThreeCardPoker={views => <ThreeCardPokerPage account={account} views={views} />} />
}

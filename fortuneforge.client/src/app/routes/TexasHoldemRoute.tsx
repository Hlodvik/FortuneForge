import { TexasHoldemBrowser } from '@fortuneforge/games-texas-holdem'
import bundledStyles from '@fortuneforge/games-texas-holdem/styles.css?inline'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { CreditTexasHoldemPage } from '../../pages/cards/texasHoldem/CreditTexasHoldemPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedTexasHoldemRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/texas-holdem')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening the credit Hold’em table…" errorTitle="The credit Hold’em table could not be opened." onRetry={reload} />
  }
  return <TexasHoldemSession key={account.userId} account={account} />
}

function TexasHoldemSession({ account }: { account: AccountSummary }) {
  const { Game, pending } = useReleasedGame('texas-holdem', 'Texas Hold’em', TexasHoldemBrowser, bundledStyles)
  if (!Game) return pending
  return <Game renderHoldem={views => <CreditTexasHoldemPage account={account} views={views} />} />
}

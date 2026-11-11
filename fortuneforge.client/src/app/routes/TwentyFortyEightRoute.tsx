import { HttpTwentyFortyEightGateway, TwentyFortyEightGame as BundledTwentyFortyEightGame } from '@fortuneforge/games-2048'
import bundledStyles from '@fortuneforge/games-2048/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { useMemo } from 'react'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { InGameShell } from '../../components/InGameShell'

export function AuthenticatedTwentyFortyEightRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/2048')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening 2048…" errorTitle="2048 could not be opened." onRetry={reload} />
  }

  return <TwentyFortyEightSession key={account.userId} account={account} />
}

function TwentyFortyEightSession({ account }: { account: AccountSummary }) {
  const gateway = useMemo(() => new HttpTwentyFortyEightGateway('/api/games/2048', fetchWithAccountSession), [])
  const { Game: TwentyFortyEightGame, pending } = useReleasedGame('2048', '2048', BundledTwentyFortyEightGame, bundledStyles)
  if (TwentyFortyEightGame === null) return pending

  return <InGameShell account={account} title="2048">
    <TwentyFortyEightGame embedded gateway={gateway} playerName={account.playerName} tableLabel="2048" />
  </InGameShell>
}

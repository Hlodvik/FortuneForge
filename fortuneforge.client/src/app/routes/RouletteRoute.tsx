import { HttpRouletteGateway, RouletteGame as BundledRouletteGame } from '@fortuneforge/games-roulette'
import bundledStyles from '@fortuneforge/games-roulette/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { useMemo } from 'react'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'
import { InGameShell } from '../../components/InGameShell'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedRouletteRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/roulette')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Roulette…" errorTitle="Roulette could not be opened." onRetry={reload} />
  }

  return <RouletteSession key={account.userId} account={account} />
}

function RouletteSession({ account }: { account: AccountSummary }) {
  const gateway = useMemo(() => new HttpRouletteGateway('/api/games/roulette', fetchWithAccountSession), [])
  const { Game: RouletteGame, pending } = useReleasedGame('roulette', 'Roulette', BundledRouletteGame, bundledStyles)
  if (RouletteGame === null) return pending

  return <InGameShell account={account} title="Roulette" className="player-page">
    <RouletteGame key={account.userId} gateway={gateway} playerId={account.userId} showTitle={false} />
  </InGameShell>
}

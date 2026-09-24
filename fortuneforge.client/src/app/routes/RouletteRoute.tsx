import { HttpRouletteGateway, RouletteGame } from '@fortuneforge/games-roulette'
import '@fortuneforge/games-roulette/styles.css'
import { useMemo } from 'react'
import { InGameShell } from '../../components/InGameShell'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedRouletteRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/roulette')
  const gateway = useMemo(() => new HttpRouletteGateway('/api/games/roulette'), [])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Roulette…" errorTitle="Roulette could not be opened." onRetry={reload} />
  }

  return <InGameShell account={account} title="Roulette" theme="casino" className="player-page">
    <RouletteGame gateway={gateway} />
  </InGameShell>
}

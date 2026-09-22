import { HttpRouletteGateway, RouletteGame } from '@fortuneforge/games-roulette'
import { useMemo } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedRouletteRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/roulette')
  const gateway = useMemo(() => new HttpRouletteGateway('/api/games/roulette'), [])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Roulette…" errorTitle="Roulette could not be opened." onRetry={reload} />
  }

  return <RouletteGame gateway={gateway} playerName={account.playerName} tableLabel="Roulette" backHref="/games" />
}

import { HttpTwentyFortyEightGateway, TwentyFortyEightGame } from '@fortuneforge/games-2048'
import { useMemo } from 'react'
import { fetchWithAccountSession } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedTwentyFortyEightRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/2048')
  const gateway = useMemo(() => new HttpTwentyFortyEightGateway('/api/games/2048', fetchWithAccountSession), [])
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening 2048…" errorTitle="2048 could not be opened." onRetry={reload} />
  }

  return <><GameAmbientMusic game="2048" /><TwentyFortyEightGame backHref="/games" gateway={gateway} playerName={account.playerName} tableLabel="2048" /></>
}

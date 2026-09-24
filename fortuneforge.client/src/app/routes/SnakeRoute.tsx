import { useMemo } from 'react'
import { LocalSnakeGateway, SnakeGame } from '@fortuneforge/games-snake'
import { InGameShell } from '../../components/InGameShell'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import '@fortuneforge/games-snake/styles.css'
import '../../pages/playerShell.css'
import './SnakeRoute.css'

export function AuthenticatedSnakeRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/snake')
  const gateway = useMemo(() => new LocalSnakeGateway(), [])
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Snake…" errorTitle="Snake could not be opened." onRetry={reload} />
  return <InGameShell account={account} title="Snake" theme="arcade" className="player-page snake-page">
    <SnakeGame gateway={gateway} />
  </InGameShell>
}

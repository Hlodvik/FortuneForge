import { useMemo } from 'react'
import { LocalSnakeGateway, SnakeGame } from '@fortuneforge/games-snake'
import { PlayerHeader } from '../../components/PlayerHeader'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import '@fortuneforge/games-snake/styles.css'
import '../../pages/playerShell.css'
import './SnakeRoute.css'

export function AuthenticatedSnakeRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/snake')
  const gateway = useMemo(() => new LocalSnakeGateway(), [])
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Snake…" errorTitle="Snake could not be opened." onRetry={reload} />
  return <div className="player-page snake-page"><GameAmbientMusic game="snake" /><PlayerHeader account={account} /><SnakeGame gateway={gateway} /></div>
}

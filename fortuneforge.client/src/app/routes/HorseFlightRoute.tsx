import { useMemo } from 'react'
import { HorseFlightGame, HttpHorseFlightGateway } from '@fortuneforge/games-horse-flight'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import '@fortuneforge/games-horse-flight/styles.css'
import '../../pages/playerShell.css'
import './HorseFlightRoute.css'

export function AuthenticatedHorseFlightRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/horse-flight')
  const gateway = useMemo(() => new HttpHorseFlightGateway('/api/games/horse-flight', fetchWithAccountSession), [])
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Horse Flight…" errorTitle="Horse Flight could not be opened." onRetry={reload} />
  return <div className="player-page horse-flight-page"><GameAmbientMusic game="horse-flight" /><PlayerHeader account={account} /><HorseFlightGame gateway={gateway} playerId={account.userId} /></div>
}

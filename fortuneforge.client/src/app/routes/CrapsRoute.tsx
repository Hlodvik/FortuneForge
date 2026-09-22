import { CrapsGame, HttpCrapsGateway } from '@fortuneforge/games-craps'
import { useMemo } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedCrapsRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/craps')
  const gateway = useMemo(() => new HttpCrapsGateway('/api/games/craps'), [])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Craps…" errorTitle="Craps could not be opened." onRetry={reload} />
  }

  return <><GameAmbientMusic game="craps" /><CrapsGame gateway={gateway} playerName={account.playerName} tableLabel="Craps" backHref="/games" /></>
}

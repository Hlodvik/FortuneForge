import { useMemo } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { HttpArcadeCompetitionGateway } from '../../games/arcade/arcadeCompetitionApi'
import { FlappyFreeRunPage } from '../../pages/games/flappy/FlappyFreeRunPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { fetchWithAccountSession } from '../../features/account/services/accountsApi'
import { InGameShell } from '../../components/InGameShell'

export function AuthenticatedFlappyRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/flappy')
  const gateway = useMemo(() => new HttpArcadeCompetitionGateway(fetchWithAccountSession), [])
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Flappy…" errorTitle="Flappy could not be opened." onRetry={reload} />
  }
  return <InGameShell account={account} title="Flappy" theme="arcade">
    <GameAmbientMusic game="flappy" />
    <FlappyFreeRunPage account={account} gateway={gateway} />
  </InGameShell>
}

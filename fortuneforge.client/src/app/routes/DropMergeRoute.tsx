import { DropMergeGame, HttpDropMergeGateway } from '@fortuneforge/games-drop-merge'
import { useMemo } from 'react'
import { fetchWithAccountSession } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedDropMergeRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/drop-merge')
  const gateway = useMemo(() => new HttpDropMergeGateway('/api/games/drop-merge', fetchWithAccountSession), [])
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Drop Merge…" errorTitle="Drop Merge could not be opened." onRetry={reload} />
  }

  return <><GameAmbientMusic game="drop-merge" /><DropMergeGame backHref="/games" gateway={gateway} playerName={account.playerName} tableLabel="Drop Merge" /></>
}

import { DropMergeGame, HttpDropMergeGateway } from '@fortuneforge/games-drop-merge'
import '@fortuneforge/games-drop-merge/styles.css'
import { useMemo } from 'react'
import { fetchWithAccountSession } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { InGameShell } from '../../components/InGameShell'

export function AuthenticatedDropMergeRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/drop-merge')
  const gateway = useMemo(() => new HttpDropMergeGateway('/api/games/drop-merge', fetchWithAccountSession), [])
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Drop Merge…" errorTitle="Drop Merge could not be opened." onRetry={reload} />
  }

  return <InGameShell account={account} title="Drop Merge" theme="arcade">
    <DropMergeGame embedded gateway={gateway} playerName={account.playerName} tableLabel="Drop Merge" />
  </InGameShell>
}

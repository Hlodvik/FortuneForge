import { DropMergeGame as BundledDropMergeGame, HttpDropMergeGateway } from '@fortuneforge/games-drop-merge'
import bundledStyles from '@fortuneforge/games-drop-merge/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { useMemo } from 'react'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { InGameShell } from '../../components/InGameShell'

export function AuthenticatedDropMergeRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/drop-merge')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Drop Merge…" errorTitle="Drop Merge could not be opened." onRetry={reload} />
  }

  return <DropMergeSession key={account.userId} account={account} />
}

function DropMergeSession({ account }: { account: AccountSummary }) {
  const gateway = useMemo(() => new HttpDropMergeGateway('/api/games/drop-merge', fetchWithAccountSession), [])
  const { Game: DropMergeGame, pending } = useReleasedGame('drop-merge', 'Drop Merge', BundledDropMergeGame, bundledStyles)
  if (DropMergeGame === null) return pending

  return <InGameShell account={account} title="Drop Merge">
    <DropMergeGame embedded gateway={gateway} playerName={account.playerName} tableLabel="Drop Merge" />
  </InGameShell>
}

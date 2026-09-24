import { CrapsGame, HttpCrapsGateway } from '@fortuneforge/games-craps'
import '@fortuneforge/games-craps/styles.css'
import { useMemo } from 'react'
import { InGameShell } from '../../components/InGameShell'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedCrapsRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/craps')
  const gateway = useMemo(() => new HttpCrapsGateway('/api/games/craps'), [])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Craps…" errorTitle="Craps could not be opened." onRetry={reload} />
  }

  return <InGameShell account={account} title="Craps" theme="casino" className="player-page">
    <CrapsGame gateway={gateway} />
  </InGameShell>
}

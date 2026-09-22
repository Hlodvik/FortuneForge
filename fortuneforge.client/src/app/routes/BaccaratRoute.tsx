import { useCallback, useEffect, useMemo, useState } from 'react'
import { BaccaratGame, HttpBaccaratGateway } from '@fortuneforge/games-baccarat'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedBaccaratRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/baccarat')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Baccarat…" errorTitle="Baccarat could not be opened." onRetry={reload} />
  }
  return <BaccaratSession initialAccount={account} />
}

function BaccaratSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const gateway = useMemo(() => new HttpBaccaratGateway('/api/games/baccarat', fetchWithAccountSession), [])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <div className="player-page">
    <PlayerHeader account={account} />
    <BaccaratGame gateway={gateway} playerId={account.userId} onBalanceChange={refreshBalance} />
  </div>
}

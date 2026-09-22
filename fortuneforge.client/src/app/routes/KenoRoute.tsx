import { useCallback, useEffect, useMemo, useState } from 'react'
import { HttpKenoGateway, KenoGame } from '@fortuneforge/games-keno'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedKenoRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/keno')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Keno…" errorTitle="Keno could not be opened." onRetry={reload} />
  }
  return <KenoSession initialAccount={account} />
}

function KenoSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const gateway = useMemo(() => new HttpKenoGateway('/api/games/keno', fetchWithAccountSession), [])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <div className="player-page">
    <PlayerHeader account={account} />
    <KenoGame gateway={gateway} playerId={account.userId} onBalanceChange={refreshBalance} />
  </div>
}

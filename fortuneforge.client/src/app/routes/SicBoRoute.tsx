import { useCallback, useEffect, useMemo, useState } from 'react'
import { HttpSicBoGateway, SicBoGame } from '@fortuneforge/games-sic-bo'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedSicBoRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/sic-bo')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Sic Bo…" errorTitle="Sic Bo could not be opened." onRetry={reload} />
  }
  return <SicBoSession initialAccount={account} />
}

function SicBoSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const gateway = useMemo(() => new HttpSicBoGateway('/api/games/sic-bo', fetchWithAccountSession), [])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <div className="player-page">
    <GameAmbientMusic game="sic-bo" />
    <PlayerHeader account={account} />
    <SicBoGame gateway={gateway} playerId={account.userId} onBalanceChange={refreshBalance} />
  </div>
}

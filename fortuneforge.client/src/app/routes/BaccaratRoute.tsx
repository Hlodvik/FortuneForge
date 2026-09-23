import { useCallback, useEffect, useMemo, useState } from 'react'
import { BaccaratGame, HttpBaccaratGateway } from '@fortuneforge/games-baccarat'
import '@fortuneforge/games-baccarat/styles.css'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { PracticeModeBanner } from './PracticeModeBanner'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'

export function AuthenticatedBaccaratRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/baccarat')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Baccarat…" errorTitle="Baccarat could not be opened." onRetry={reload} />
  }
  return <BaccaratSession initialAccount={account} />
}

function BaccaratSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpBaccaratGateway('/api/games/baccarat', practiceAccountFetch(fetchWithAccountSession))
    : new HttpBaccaratGateway('/api/games/baccarat', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <div className="player-page">
    <PlayerHeader account={account} />
    <PracticeModeBanner enabled={practiceMode} path="/games/baccarat" />
    <BaccaratGame gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} onBalanceChange={practiceMode ? undefined : refreshBalance} />
  </div>
}

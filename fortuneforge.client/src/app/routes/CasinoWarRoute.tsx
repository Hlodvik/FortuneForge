import { useCallback, useEffect, useMemo, useState } from 'react'
import { CasinoWarGame, HttpCasinoWarGateway } from '@fortuneforge/games-casino-war'
import '@fortuneforge/games-casino-war/styles.css'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { PracticeModeBanner } from './PracticeModeBanner'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'

export function AuthenticatedCasinoWarRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/casino-war')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Casino War…" errorTitle="Casino War could not be opened." onRetry={reload} />
  }
  return <CasinoWarSession initialAccount={account} />
}

function CasinoWarSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpCasinoWarGateway('/api/games/casino-war', practiceAccountFetch(fetchWithAccountSession))
    : new HttpCasinoWarGateway('/api/games/casino-war', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <div className="player-page">
    <PlayerHeader account={account} />
    <PracticeModeBanner enabled={practiceMode} path="/games/casino-war" />
    <CasinoWarGame gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} onBalanceChange={practiceMode ? undefined : refreshBalance} />
  </div>
}

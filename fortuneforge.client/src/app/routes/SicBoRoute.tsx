import { useCallback, useEffect, useMemo, useState } from 'react'
import { HttpSicBoGateway, SicBoGame } from '@fortuneforge/games-sic-bo'
import '@fortuneforge/games-sic-bo/styles.css'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { WalletPracticeMode } from './WalletPracticeMode'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'

export function AuthenticatedSicBoRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/sic-bo')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Sic Bo…" errorTitle="Sic Bo could not be opened." onRetry={reload} />
  }
  return <SicBoSession initialAccount={account} />
}

function SicBoSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpSicBoGateway('/api/games/sic-bo', practiceAccountFetch(fetchWithAccountSession))
    : new HttpSicBoGateway('/api/games/sic-bo', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <div className="player-page">
    <GameAmbientMusic game="sic-bo" />
    <PlayerHeader account={account} />
    <WalletPracticeMode enabled={practiceMode} path="/games/sic-bo" />
    <SicBoGame gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} onBalanceChange={practiceMode ? undefined : refreshBalance} />
  </div>
}

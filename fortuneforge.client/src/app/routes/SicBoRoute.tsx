import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { HttpSicBoGateway, SicBoGame } from '@fortuneforge/games-sic-bo'
import '@fortuneforge/games-sic-bo/styles.css'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { PracticeModeNavAction } from './PracticeModeBanner'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'
import './SicBoRoute.css'

export function AuthenticatedSicBoRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/sic-bo')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Sic Bo…" errorTitle="Sic Bo could not be opened." onRetry={reload} />
  }
  return <SicBoSession key={account.userId} initialAccount={account} />
}

function SicBoSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const mounted = useRef(false)
  const balanceRevision = useRef(0)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpSicBoGateway('/api/games/sic-bo', practiceAccountFetch(fetchWithAccountSession))
    : new HttpSicBoGateway('/api/games/sic-bo', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])
  const refreshBalance = useCallback((balance: number) => {
    const revision = ++balanceRevision.current
    setAccount(current => ({ ...current, balances: { ...current.balances, slotsCredits: balance } }))
    void getCurrentAccount().then(next => {
      if (mounted.current && revision === balanceRevision.current && next.userId === initialAccount.userId) setAccount(next)
    }).catch(() => undefined)
  }, [initialAccount.userId])

  return <InGameShell account={account} title="Sic Bo" theme="casino" className="player-page sic-bo-page" actions={<PracticeModeNavAction enabled={practiceMode} path="/games/sic-bo" />}>
    <SicBoGame gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} roundOwnerId={account.userId} recoveryMode={practiceMode ? 'practice' : 'account'} showTitle={false} onBalanceChange={practiceMode ? undefined : refreshBalance} />
  </InGameShell>
}

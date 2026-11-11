import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { HttpSicBoGateway, SicBoGame as BundledSicBoGame } from '@fortuneforge/games-sic-bo'
import bundledStyles from '@fortuneforge/games-sic-bo/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
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

  const { Game: SicBoGame, pending } = useReleasedGame('sic-bo', 'Sic Bo', BundledSicBoGame, bundledStyles)
  if (SicBoGame === null) return pending

  return <InGameShell account={account} title="Sic Bo" className="player-page sic-bo-page">
    <SicBoGame gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} roundOwnerId={account.userId} recoveryMode={practiceMode ? 'practice' : 'account'} showTitle onBalanceChange={practiceMode ? undefined : refreshBalance} />
  </InGameShell>
}

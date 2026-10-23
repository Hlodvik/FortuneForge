// Copy to fortuneforge.client/src/app/routes/AndarBaharRoute.tsx during batch integration.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AndarBaharGame, HttpAndarBaharGateway } from '@fortuneforge/games-andar-bahar'
import '@fortuneforge/games-andar-bahar/styles.css'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { PracticeModeNavAction } from './PracticeModeBanner'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'
import './AndarBaharRoute.css'

export function AuthenticatedAndarBaharRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/andar-bahar')
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Andar Bahar…" errorTitle="Andar Bahar could not be opened." onRetry={reload}/>
  return <AndarBaharSession key={account.userId} initialAccount={account}/>
}
function AndarBaharSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const active = useRef(true)
  const identity = useRef(initialAccount.userId); identity.current = initialAccount.userId
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => new HttpAndarBaharGateway('/api/games/andar-bahar', practiceMode ? practiceAccountFetch(fetchWithAccountSession) : fetchWithAccountSession), [practiceMode])
  useEffect(() => setAccount(initialAccount), [initialAccount])
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const refreshBalance = useCallback(() => {
    const requestedUser = initialAccount.userId
    void getCurrentAccount().then(next => {
      if (active.current && identity.current === requestedUser && next.userId === requestedUser) setAccount(next)
    }).catch(() => undefined)
  }, [initialAccount.userId])
  const scope = `${initialAccount.userId}:${practiceMode ? 'practice' : 'account'}`
  return <InGameShell account={account} title="Andar Bahar" theme="casino" className="player-page andar-bahar-route" bodyClassName="andar-bahar-route__body" actions={<PracticeModeNavAction enabled={practiceMode} path="/games/andar-bahar"/>}>
    <AndarBaharGame key={scope} playerId={scope} gateway={gateway} onBalanceChange={practiceMode ? undefined : refreshBalance}/>
  </InGameShell>
}

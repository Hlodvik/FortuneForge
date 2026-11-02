import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { HttpMinesGateway, MinesGame } from '@fortuneforge/games-mines'
import '@fortuneforge/games-mines/styles.css'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import './MinesRoute.css'

export function AuthenticatedMinesRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/mines')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Mines…"
      errorTitle="Mines could not be opened." onRetry={reload} />
  }
  return <MinesAccountSession key={account.userId} initialAccount={account} />
}

function MinesAccountSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const active = useRef(true)
  const refreshGeneration = useRef(0)
  const identity = useRef(initialAccount.userId)
  identity.current = initialAccount.userId
  // This host route only uses real account credits. Server-side paid enablement is a separate gate.
  const gateway = useMemo(() => new HttpMinesGateway('/api/games/mines', fetchWithAccountSession), [])
  const scope = `${initialAccount.userId}:account`

  useEffect(() => { refreshGeneration.current++; setAccount(initialAccount) }, [initialAccount])
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const refreshBalance = useCallback(() => {
    const requestedUser = initialAccount.userId
    const requestedGeneration = ++refreshGeneration.current
    // The game's callback is integer cents; the shared account summary owns currency-unit balances.
    void getCurrentAccount().then(next => {
      if (active.current && refreshGeneration.current === requestedGeneration
        && identity.current === requestedUser && next.userId === requestedUser) setAccount(next)
    }).catch(() => undefined)
  }, [initialAccount.userId])

  return <InGameShell account={account} reportTitle="Mines" className="mines-page">
    <MinesGame key={scope} playerId={scope} gateway={gateway} currencySymbol="R" onBalanceChange={refreshBalance} />
  </InGameShell>
}

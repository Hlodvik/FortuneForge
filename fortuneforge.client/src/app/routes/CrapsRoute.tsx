import { CrapsGame as BundledCrapsGame, HttpCrapsGateway } from '@fortuneforge/games-craps'
import bundledStyles from '@fortuneforge/games-craps/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedCrapsRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/craps')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Craps…" errorTitle="Craps could not be opened." onRetry={reload} />
  }

  return <CrapsSession key={account.userId} initialAccount={account} />
}

function CrapsSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const gateway = useMemo(() => new HttpCrapsGateway('/api/games/craps', fetchWithAccountSession), [])
  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(next => {
      if (next.userId === initialAccount.userId) setAccount(next)
    }).catch(() => undefined)
  }, [initialAccount.userId])

  const { Game: CrapsGame, pending } = useReleasedGame('craps', 'Craps', BundledCrapsGame, bundledStyles)
  if (CrapsGame === null) return pending

  return <InGameShell account={account} title="Craps" className="craps-page" bodyClassName="craps-route__body">
    <CrapsGame playerId={account.userId} gateway={gateway} requirePaid onBalanceChange={refreshBalance} />
  </InGameShell>
}

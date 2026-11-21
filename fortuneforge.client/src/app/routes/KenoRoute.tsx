import { useCallback, useEffect, useMemo, useState } from 'react'
import { HttpKenoGateway, KenoGame as BundledKenoGame } from '@fortuneforge/games-keno'
import bundledStyles from '@fortuneforge/games-keno/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedKenoRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/keno')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Keno…" errorTitle="Keno could not be opened." onRetry={reload} />
  }
  return <KenoSession key={account.userId} initialAccount={account} />
}

function KenoSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const gateway = useMemo(() => new HttpKenoGateway('/api/games/keno', fetchWithAccountSession, initialAccount.userId), [initialAccount.userId])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(next => { if (next.userId === initialAccount.userId) setAccount(next) }).catch(() => undefined)
  }, [initialAccount.userId])

  const { Game: KenoGame, pending } = useReleasedGame('keno', 'Keno', BundledKenoGame, bundledStyles)
  if (KenoGame === null) return pending

  return <InGameShell account={account} title="Keno" className="player-page">
    <KenoGame gateway={gateway} playerId={account.userId} onBalanceChange={refreshBalance} />
  </InGameShell>
}

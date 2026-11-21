import { createGameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import { useMemo } from 'react'
import { HttpSnakeGateway, SnakeGame } from '@fortuneforge/games-snake'
import { InGameShell } from '../../components/InGameShell'
import { createAccountBoundGameFetch, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import '@fortuneforge/games-snake/styles.css'
import '../../pages/playerShell.css'
import './SnakeRoute.css'

export function AuthenticatedSnakeRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/snake')
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Snake…" errorTitle="Snake could not be opened." onRetry={reload} />
  return <SnakeAccountSession key={account.userId} account={account} />
}

function SnakeAccountSession({ account }: Readonly<{ account: AccountSummary }>) {
  const gateway = useMemo(() => new HttpSnakeGateway('/api/games/snake',
    createAccountBoundGameFetch(account.userId),
    createGameCommandRecovery({ accountId: account.userId, gameId: 'snake', routePrefix: '/api/games/snake' }),
    account.userId,
  ), [account.userId])
  return <InGameShell account={account} title="Snake" className="player-page snake-page">
    <SnakeGame gateway={gateway} />
  </InGameShell>
}

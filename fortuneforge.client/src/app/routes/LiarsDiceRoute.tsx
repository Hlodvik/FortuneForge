import { createGameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import { HttpLiarsDiceGateway, LiarsDiceGame } from '@fortuneforge/games-liars-dice'
import '@fortuneforge/games-liars-dice/styles.css'
import { useMemo } from 'react'
import { InGameShell } from '../../components/InGameShell'
import { createAccountBoundGameFetch, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedLiarsDiceRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/liars-dice')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Liar’s Dice…" errorTitle="Liar’s Dice could not be opened." onRetry={reload} />
  }
  return <LiarsDiceAccountSession key={account.userId} account={account} />
}

function LiarsDiceAccountSession({ account }: Readonly<{ account: AccountSummary }>) {
  const gateway = useMemo(() => new HttpLiarsDiceGateway(
    '/api/games/liars-dice',
    createAccountBoundGameFetch(account.userId),
    createGameCommandRecovery({ accountId: account.userId, gameId: 'liars-dice', routePrefix: '/api/games/liars-dice' }),
    account.userId,
  ), [account.userId])
  return <InGameShell account={account} title="Liar’s Dice" className="player-page">
    <GameAmbientMusic game="liars-dice" />
    <LiarsDiceGame gateway={gateway} playerId={account.userId} showTitle={false} />
  </InGameShell>
}

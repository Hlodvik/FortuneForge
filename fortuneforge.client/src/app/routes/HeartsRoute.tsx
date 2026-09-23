import { HeartsGame, HttpHeartsGateway } from '@fortuneforge/games-hearts'
import '@fortuneforge/games-hearts/styles.css'
import { useMemo } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedHeartsRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/hearts')
  const gateway = useMemo(() => new HttpHeartsGateway('/api/games/hearts'), [])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Hearts…" errorTitle="Hearts could not be opened." onRetry={reload} />
  }

  return <HeartsGame gateway={gateway} playerName={account.playerName} tableLabel="Hearts" backHref="/games" />
}

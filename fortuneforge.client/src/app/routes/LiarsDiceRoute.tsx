import { HttpLiarsDiceGateway, LiarsDiceGame } from '@fortuneforge/games-liars-dice'
import '@fortuneforge/games-liars-dice/styles.css'
import { useMemo } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedLiarsDiceRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/liars-dice')
  const gateway = useMemo(() => new HttpLiarsDiceGateway('/api/games/liars-dice'), [])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Liar’s Dice…" errorTitle="Liar’s Dice could not be opened." onRetry={reload} />
  }

  return <><GameAmbientMusic game="liars-dice" /><LiarsDiceGame gateway={gateway} playerName={account.playerName} tableLabel="Liar’s Dice" backHref="/games" /></>
}

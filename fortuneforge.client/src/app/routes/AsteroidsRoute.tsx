import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import {
  AccountRequestError,
  clearAccountToken,
  fetchWithAccountSession,
  getCurrentAccount,
} from '../../features/account/services/accountsApi'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { HttpArcadeCompetitionGateway } from '../../games/arcade/arcadeCompetitionApi'
import { GameAmbientMusic } from '../../features/audio/GameAmbientMusic'
import { InGameShell } from '../../components/InGameShell'
import { AsteroidsCompetitionPage } from '../../pages/games/asteroids/AsteroidsCompetitionPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { synchronizeAsteroidsRouteAccount } from './asteroidsRouteAccount'

const returnPath = '/games/asteroids'

export function AuthenticatedAsteroidsRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/asteroids')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening the Asteroids competition…" errorTitle="The Asteroids competition could not be opened." onRetry={reload} />
  }

  return <AsteroidsCompetitionSession initialAccount={account} />
}

function AsteroidsCompetitionSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [currentAccount, setCurrentAccount] = useState(initialAccount)
  const lastAuthenticatedAccount = useRef(initialAccount)
  const gateway = useMemo(() => new HttpArcadeCompetitionGateway(fetchWithAccountSession), [])

  useEffect(() => {
    const hasNewAuthenticatedSnapshot = lastAuthenticatedAccount.current !== initialAccount
    lastAuthenticatedAccount.current = initialAccount
    setCurrentAccount((current) => synchronizeAsteroidsRouteAccount(
      current,
      initialAccount,
      hasNewAuthenticatedSnapshot,
    ))
  }, [initialAccount])

  const refreshAccount = useCallback(async () => {
    try {
      setCurrentAccount(await getCurrentAccount())
    } catch (reason: unknown) {
      if (reason instanceof AccountRequestError && reason.status === 401) {
        clearAccountToken()
        window.location.replace(`/login?returnTo=${encodeURIComponent(returnPath)}`)
        return
      }
      throw reason
    }
  }, [])

  return <InGameShell account={currentAccount} title="Asteroids" theme="arcade">
    <GameAmbientMusic game="asteroids" />
    <AsteroidsCompetitionPage account={currentAccount} gateway={gateway} onPaidAccountRefresh={refreshAccount} />
  </InGameShell>
}

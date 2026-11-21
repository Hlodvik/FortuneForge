import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { HttpArcadeCompetitionGateway } from '../../games/arcade/arcadeCompetitionApi'
import { FlappyFreeRunPage } from '../../pages/games/flappy/FlappyFreeRunPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { AccountRequestError, createAccountBoundGameFetch, fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { InGameShell } from '../../components/InGameShell'

export function AuthenticatedFlappyRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/flappy')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Flappy…" errorTitle="Flappy could not be opened." onRetry={reload} />
  }
  return <FlappyAccountSession key={account.userId} initialAccount={account} reload={reload} />
}

function FlappyAccountSession({ initialAccount, reload }: Readonly<{ initialAccount: AccountSummary; reload: () => void }>) {
  const [account, setAccount] = useState(initialAccount)
  const active = useRef(true)
  const refreshGeneration = useRef(0)
  useEffect(() => { refreshGeneration.current++; setAccount(initialAccount) }, [initialAccount])
  useEffect(() => { active.current = true; return () => { active.current = false; refreshGeneration.current++ } }, [])
  const refreshPaidAccount = useCallback(async () => {
    const generation = ++refreshGeneration.current
    try {
      const next = await getCurrentAccount()
      if (!active.current || generation !== refreshGeneration.current) return
      if (next.userId !== initialAccount.userId) reload()
      else setAccount(next)
    } catch (reason) {
      if (active.current && generation === refreshGeneration.current && reason instanceof AccountRequestError && reason.status === 401) reload()
    }
  }, [initialAccount.userId, reload])
  const gateway = useMemo(() => new HttpArcadeCompetitionGateway(
    createAccountBoundGameFetch(initialAccount.userId, fetchWithAccountSession),
  ), [initialAccount.userId])
  return <InGameShell account={account} title="Flappy" className="flappy-shell">
    <FlappyFreeRunPage account={account} gateway={gateway} onPaidAccountRefresh={refreshPaidAccount} />
  </InGameShell>
}

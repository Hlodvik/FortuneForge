import { lazy, useCallback } from 'react'
import type { NeonNightsGameProps, NeonShellProps } from '@fortuneforge/games-neon-nights'
import { InGameShell } from '../../components/InGameShell'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'
import { useGameRelease } from '../../features/game-releases/useGameRelease'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

const BundledNeonNights = lazy(() => import('./NeonNightsBundled'))

/** The app owns account access and chrome. All Neon gameplay belongs to its package. */
export function NeonNightsHost({ account, demoMode = false, onSpinStateChange }: {
  account?: AccountSummary
  demoMode?: boolean
  onSpinStateChange?: (isSpinning: boolean) => void
}) {
  const { state, retry } = useGameRelease<NeonNightsGameProps>('neon-nights')
  const renderShell = useCallback(({ balance, onNavigate, children }: NeonShellProps) => <InGameShell
    account={account} authenticated={!demoMode} reportTitle="Neon Nights" balance={balance}
    onNavigate={onNavigate} bodyClassName="in-game-shell__body--slots"
  >{children}</InGameShell>, [account, demoMode])
  if (state.status === 'loading' || state.status === 'error') {
    return <AuthenticatedRouteState error={state.status === 'error' ? state.error : null}
      loadingLabel="Opening Neon Nights…" errorTitle="Neon Nights could not be opened." onRetry={retry} />
  }
  const Game = state.status === 'ready' ? state.Game : BundledNeonNights
  return <Game account={account} demoMode={demoMode} authenticatedFetch={fetchWithAccountSession}
    renderShell={renderShell} paymentAlerts={demoMode ? undefined : <PaymentAlertsMenu />} onSpinStateChange={onSpinStateChange} />
}

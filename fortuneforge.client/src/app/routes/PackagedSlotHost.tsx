import { useCallback, type ComponentType, type ReactNode } from 'react'
import { InGameShell } from '../../components/InGameShell'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'
import { useGameRelease } from '../../features/game-releases/useGameRelease'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export type PackagedSlotHostProps = {
  account?: AccountSummary
  demoMode?: boolean
  onSpinStateChange?: (isSpinning: boolean) => void
}

type GameShellProps = { balance: number; onNavigate: () => void; children: ReactNode }

type SlotGameProps = PackagedSlotHostProps & {
  authenticatedFetch: typeof fetchWithAccountSession
  renderShell: (props: GameShellProps) => ReactNode
  paymentAlerts?: ReactNode
}

export function PackagedSlotHost({ gameId, title, BundledGame, account, demoMode = false, showPaymentAlerts = true, onSpinStateChange }: PackagedSlotHostProps & {
  gameId: string
  title: string
  BundledGame: ComponentType<SlotGameProps>
  showPaymentAlerts?: boolean
}) {
  const { state, retry } = useGameRelease<SlotGameProps>(gameId)
  const renderShell = useCallback(({ balance, onNavigate, children }: GameShellProps) => <InGameShell
    account={account} authenticated={!demoMode} reportTitle={title} balance={balance}
    onNavigate={onNavigate} bodyClassName="in-game-shell__body--slots"
  >{children}</InGameShell>, [account, demoMode, title])

  if (state.status === 'loading' || state.status === 'error') {
    return <AuthenticatedRouteState error={state.status === 'error' ? state.error : null}
      loadingLabel={'Opening ' + title + '…'} errorTitle={title + ' could not be opened.'} onRetry={retry} />
  }
  const Game = state.status === 'ready' ? state.Game : BundledGame
  return <Game account={account} demoMode={demoMode} authenticatedFetch={fetchWithAccountSession}
    renderShell={renderShell} paymentAlerts={!demoMode && showPaymentAlerts ? <PaymentAlertsMenu /> : undefined}
    onSpinStateChange={onSpinStateChange} />
}

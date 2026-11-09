import { useCallback } from 'react'
import { NeonNightsGame, type NeonShellProps } from '@fortuneforge/games-neon-nights'
import { InGameShell } from '../../components/InGameShell'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'

/** The app owns account access and chrome. All Neon gameplay belongs to its package. */
export function NeonNightsHost({ account, demoMode = false, onSpinStateChange }: {
  account?: AccountSummary
  demoMode?: boolean
  onSpinStateChange?: (isSpinning: boolean) => void
}) {
  const renderShell = useCallback(({ balance, onNavigate, children }: NeonShellProps) => <InGameShell
    account={account} authenticated={!demoMode} reportTitle="Neon Nights" balance={balance}
    onNavigate={onNavigate} bodyClassName="in-game-shell__body--slots"
  >{children}</InGameShell>, [account, demoMode])
  return <NeonNightsGame account={account} demoMode={demoMode} authenticatedFetch={fetchWithAccountSession}
    renderShell={renderShell} paymentAlerts={demoMode ? undefined : <PaymentAlertsMenu />} onSpinStateChange={onSpinStateChange} />
}

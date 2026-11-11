import { useCallback } from 'react'
import { GoldFoundryGame, type GoldFoundryShellProps } from '@fortuneforge/games-gold-foundry'
import { InGameShell } from '../../components/InGameShell'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'

/** The app owns account access and shared chrome. All Gold Foundry gameplay belongs to its package. */
export function GoldFoundryHost({ account, demoMode = false, onSpinStateChange }: {
  account?: AccountSummary
  demoMode?: boolean
  onSpinStateChange?: (isSpinning: boolean) => void
}) {
  const renderShell = useCallback(({ balance, onNavigate, children }: GoldFoundryShellProps) => <InGameShell
    account={account} authenticated={!demoMode} reportTitle="Gold Foundry" balance={balance}
    onNavigate={onNavigate} bodyClassName="in-game-shell__body--slots"
  >{children}</InGameShell>, [account, demoMode])

  return <GoldFoundryGame account={account} demoMode={demoMode} authenticatedFetch={fetchWithAccountSession}
    renderShell={renderShell} paymentAlerts={demoMode ? undefined : <PaymentAlertsMenu />}
    onSpinStateChange={onSpinStateChange} />
}

import { useCallback } from 'react'
import { MidnightVaultGame, type MidnightShellProps } from '@fortuneforge/games-midnight-vault'
import { InGameShell } from '../../components/InGameShell'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'

/** The app owns account access and shared chrome. All Midnight gameplay belongs to its package. */
export function MidnightVaultHost({ account, demoMode = false, onSpinStateChange }: {
  account?: AccountSummary
  demoMode?: boolean
  onSpinStateChange?: (isSpinning: boolean) => void
}) {
  const renderShell = useCallback(({ balance, onNavigate, children }: MidnightShellProps) => <InGameShell
    account={account} authenticated={!demoMode} reportTitle="Midnight Vault" balance={balance}
    onNavigate={onNavigate} bodyClassName="in-game-shell__body--slots"
  >{children}</InGameShell>, [account, demoMode])

  return <MidnightVaultGame account={account} demoMode={demoMode} authenticatedFetch={fetchWithAccountSession}
    renderShell={renderShell} paymentAlerts={demoMode ? undefined : <PaymentAlertsMenu />}
    onSpinStateChange={onSpinStateChange} />
}

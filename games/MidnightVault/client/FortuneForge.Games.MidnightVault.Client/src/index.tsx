import { MidnightVaultView } from './MidnightVaultView.js'
import { useMidnightVault } from './useMidnightVault.js'
import type { MidnightVaultGameProps } from './types.js'

export type {
  MidnightAccount,
  MidnightAuthenticatedFetch,
  MidnightShellProps,
  MidnightVaultGameProps,
} from './types.js'

/** Complete Midnight Vault client. The application host supplies only session transport and shared chrome. */
export function MidnightVaultGame(props: MidnightVaultGameProps) {
  return <MidnightVaultView controller={useMidnightVault(props)} renderShell={props.renderShell}
    paymentAlerts={props.paymentAlerts} />
}

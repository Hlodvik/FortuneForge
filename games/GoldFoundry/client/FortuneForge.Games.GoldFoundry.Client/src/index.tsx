import { GoldFoundryView } from './GoldFoundryView.js'
import { useGoldFoundry } from './useGoldFoundry.js'
import type { GoldFoundryGameProps } from './types.js'

export type {
  GoldFoundryAccount,
  GoldFoundryAuthenticatedFetch,
  GoldFoundryGameProps,
  GoldFoundryShellProps,
} from './types.js'

/** Complete Gold Foundry client. The application host supplies only session transport and shared chrome. */
export function GoldFoundryGame(props: GoldFoundryGameProps) {
  return <GoldFoundryView controller={useGoldFoundry(props)} renderShell={props.renderShell}
    paymentAlerts={props.paymentAlerts} />
}

import { NeonNightsView } from './NeonNightsView.js'
import { useNeonNights } from './useNeonNights.js'
import type { NeonNightsGameProps } from './types.js'
export type { NeonNightsGameProps, NeonAccount, NeonAuthenticatedFetch, NeonShellProps } from './types.js'
export function NeonNightsGame(props: NeonNightsGameProps) {
  return <NeonNightsView controller={useNeonNights(props)} renderShell={props.renderShell} paymentAlerts={props.paymentAlerts} />
}

import { CrookedRoadView } from './CrookedRoadView'
import { useCrookedRoad } from './useCrookedRoad'
import type { CrookedRoadGameProps } from './types'

export type { CrookedRoadGameProps, CrookedRoadAccount, CrookedRoadAuthenticatedFetch, CrookedRoadShellProps } from './types'

export function CrookedRoadGame(props: CrookedRoadGameProps) {
  if (props.demoMode === true) throw new Error('The Crooked Road supports paid gameplay only.')
  const controller = useCrookedRoad(props)
  const content = <CrookedRoadView controller={controller} paymentAlerts={props.paymentAlerts} />
  return props.renderShell
    ? props.renderShell({ balance: controller.balance, onNavigate: controller.onNavigate, children: content })
    : content
}

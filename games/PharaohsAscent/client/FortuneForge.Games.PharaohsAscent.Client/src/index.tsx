import { PharaohAscentView } from './PharaohAscentView'
import { usePharaohAscent } from './usePharaohAscent'
import type { PharaohAscentGameProps } from './types'

export type { PharaohAscentGameProps } from './types'

export function PharaohAscentGame(props: PharaohAscentGameProps) {
  if (props.demoMode === true) throw new Error('Pharaoh’s Ascent supports paid gameplay only.')
  const controller = usePharaohAscent(props)
  const content = <PharaohAscentView controller={controller} paymentAlerts={props.paymentAlerts} />
  return props.renderShell ? props.renderShell({ balance: controller.balance, onNavigate: controller.onNavigate, children: content }) : content
}

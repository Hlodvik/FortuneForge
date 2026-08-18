import { SlotsPageView } from '../../features/slots/SlotsPageView'
import {
  useSlotsPageController,
  type SlotsPageProps,
} from '../../features/slots/useSlotsPageController'
import '../../features/slots/styles/page.css'
import '../../features/slots/styles/dialogs.css'
import '../../features/slots/styles/pageOverrides.css'

export function SlotsPage(props: SlotsPageProps) {
  const controller = useSlotsPageController(props)

  return <SlotsPageView {...controller} />
}

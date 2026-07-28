import { SlotsPageView } from './SlotsPageView'
import {
  useSlotsPageController,
  type SlotsPageProps,
} from './useSlotsPageController'

export function SlotsPage(props: SlotsPageProps) {
  const controller = useSlotsPageController(props)

  return <SlotsPageView {...controller} />
}

import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'

const CrookedRoad = lazy(() => import('./CrookedRoadBundled'))

export function CrookedRoadHost(props: Omit<PackagedSlotHostProps, 'demoMode'>) {
  return <PackagedSlotHost {...props} demoMode={false} gameId="crooked-road" title="The Crooked Road"
    BundledGame={CrookedRoad} showPaymentAlerts={false} />
}

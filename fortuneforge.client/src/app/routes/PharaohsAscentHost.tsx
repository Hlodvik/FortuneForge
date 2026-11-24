import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'

const Pharaoh = lazy(() => import('./PharaohsAscentBundled'))

export function PharaohsAscentHost(props: Omit<PackagedSlotHostProps, 'demoMode'>) {
  return <PackagedSlotHost {...props} demoMode={false} gameId="pharaohs-ascent" title="Pharaoh’s Ascent"
    BundledGame={Pharaoh} showPaymentAlerts={false} />
}

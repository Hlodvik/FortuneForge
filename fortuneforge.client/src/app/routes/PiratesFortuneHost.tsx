import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'
const Game = lazy(() => import('./PiratesFortuneBundled'))
export function PiratesFortuneHost(props: Omit<PackagedSlotHostProps, 'demoMode'>) {
  return <PackagedSlotHost {...props} demoMode={false} gameId="pirates-fortune" title="Treasure Cove" BundledGame={Game} showPaymentAlerts={false} />
}

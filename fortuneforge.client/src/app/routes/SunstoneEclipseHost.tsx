import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'
const Game = lazy(() => import('./SunstoneEclipseBundled'))
export function SunstoneEclipseHost(props: Omit<PackagedSlotHostProps, 'demoMode'>) {
  return <PackagedSlotHost {...props} demoMode={false} gameId="robot-revolution" title="Sunstone Eclipse" BundledGame={Game} showPaymentAlerts={false} />
}

import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'

const Arcane = lazy(() => import('./ArcaneArchivesBundled'))

export function ArcaneArchivesHost(props: Omit<PackagedSlotHostProps, 'demoMode'>) {
  return <PackagedSlotHost {...props} demoMode={false} gameId="arcane-archives" title="Arcane Archives"
    BundledGame={Arcane} showPaymentAlerts={false} />
}

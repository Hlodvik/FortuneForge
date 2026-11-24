import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'

const Frontier = lazy(() => import('./FrontierFortuneBundled'))

export function FrontierFortuneHost(props: Omit<PackagedSlotHostProps, 'demoMode'>) {
  return <PackagedSlotHost {...props} demoMode={false} gameId="high-noon-fortune" title="Frontier Fortune" BundledGame={Frontier} />
}

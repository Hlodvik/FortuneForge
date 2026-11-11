import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'

const BundledGame = lazy(() => import('./GoldFoundryBundled'))

export function GoldFoundryHost(props: PackagedSlotHostProps) {
  return <PackagedSlotHost {...props} gameId="gold-foundry" title="Gold Foundry" BundledGame={BundledGame} />
}

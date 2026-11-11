import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'

const BundledGame = lazy(() => import('./NeonNightsBundled'))

export function NeonNightsHost(props: PackagedSlotHostProps) {
  return <PackagedSlotHost {...props} gameId="neon-nights" title="Neon Nights" BundledGame={BundledGame} />
}

import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'
const Game = lazy(() => import('./GodsOfOlympusBundled'))
export function GodsOfOlympusHost(props: Omit<PackagedSlotHostProps, 'demoMode'>) {
  return <PackagedSlotHost {...props} demoMode={false} gameId="gods-of-olympus" title="Gods of Olympus" BundledGame={Game} />
}

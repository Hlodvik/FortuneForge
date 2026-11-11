import { lazy } from 'react'
import { PackagedSlotHost, type PackagedSlotHostProps } from './PackagedSlotHost'

const BundledGame = lazy(() => import('./MidnightVaultBundled'))

export function MidnightVaultHost(props: PackagedSlotHostProps) {
  return <PackagedSlotHost {...props} gameId="midnight-vault" title="Midnight Vault" BundledGame={BundledGame} />
}

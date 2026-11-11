import type { SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> => import.meta.env.DEV
  ? import('../dragonHoard/manifest').then((module) => module.DRAGON_HOARD_SLOT_GAME)
  : Promise.reject(new Error('Midnight Vault is loaded through its standalone route host.'))

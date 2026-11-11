import type { SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> => import.meta.env.DEV
  ? import('../goldFoundry/manifest').then((module) => module.GOLD_FOUNDRY_SLOT_GAME)
  : Promise.reject(new Error('Gold Foundry is loaded through its standalone route host.'))

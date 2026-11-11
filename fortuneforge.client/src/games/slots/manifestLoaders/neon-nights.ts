import type { SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> => import.meta.env.DEV
  ? import('../neonNights/manifest').then((module) => module.NEON_NIGHTS_SLOT_GAME)
  : Promise.reject(new Error('Neon Nights is loaded through its standalone route host.'))

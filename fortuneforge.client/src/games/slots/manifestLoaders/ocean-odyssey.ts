import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-ocean-odyssey/manifest').then((module) => adoptPackagedSlotGameManifest(module.OCEAN_ODYSSEY_SLOT_GAME))

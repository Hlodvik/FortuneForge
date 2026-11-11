import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-gods-of-olympus/manifest').then((module) => adoptPackagedSlotGameManifest(module.GODS_OF_OLYMPUS_SLOT_GAME))

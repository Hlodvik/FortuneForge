import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-reel-riches/manifest').then((module) => adoptPackagedSlotGameManifest(module.REEL_RICHES_SLOT_GAME))

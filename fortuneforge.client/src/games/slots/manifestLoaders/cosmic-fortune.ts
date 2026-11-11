import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-cosmic-fortune/manifest').then((module) => adoptPackagedSlotGameManifest(module.COSMIC_FORTUNE_SLOT_GAME))

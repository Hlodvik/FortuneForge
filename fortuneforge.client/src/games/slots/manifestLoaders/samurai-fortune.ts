import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-samurai-fortune/manifest').then((module) => adoptPackagedSlotGameManifest(module.SAMURAI_FORTUNE_SLOT_GAME))

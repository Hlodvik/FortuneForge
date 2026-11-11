import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-high-noon-fortune/manifest').then((module) => adoptPackagedSlotGameManifest(module.HIGH_NOON_FORTUNE_SLOT_GAME))

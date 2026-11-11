import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-pirates-fortune/manifest').then((module) => adoptPackagedSlotGameManifest(module.PIRATES_FORTUNE_SLOT_GAME))

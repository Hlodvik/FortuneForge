import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-frontier-fortune/manifest').then((module) => adoptPackagedSlotGameManifest(module.FRONTIER_FORTUNE_SLOT_GAME))

import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-desert-treasures/manifest').then((module) => adoptPackagedSlotGameManifest(module.DESERT_TREASURES_SLOT_GAME))

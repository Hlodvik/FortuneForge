import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-wukong/manifest').then((module) => adoptPackagedSlotGameManifest(module.WUKONG_SLOT_GAME))

import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-royal-draw/manifest').then((module) => adoptPackagedSlotGameManifest(module.ROYAL_DRAW_SLOT_GAME))

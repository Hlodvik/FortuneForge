import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-nordic-legends/manifest').then((module) => adoptPackagedSlotGameManifest(module.NORDIC_LEGENDS_SLOT_GAME))

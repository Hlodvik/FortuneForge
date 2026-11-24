import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-pharaohs-ascent/manifest').then((module) => adoptPackagedSlotGameManifest(module.PHARAOHS_ASCENT_SLOT_GAME))

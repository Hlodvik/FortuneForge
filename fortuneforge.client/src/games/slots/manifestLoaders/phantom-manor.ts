import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-phantom-manor/manifest').then((module) => adoptPackagedSlotGameManifest(module.PHANTOM_MANOR_SLOT_GAME))

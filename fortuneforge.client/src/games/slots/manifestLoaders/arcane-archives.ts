import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-arcane-archives/manifest').then((module) => adoptPackagedSlotGameManifest(module.ARCANE_ARCHIVES_SLOT_GAME))

import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-crooked-road/manifest').then(module => adoptPackagedSlotGameManifest(module.CROOKED_ROAD_SLOT_GAME))

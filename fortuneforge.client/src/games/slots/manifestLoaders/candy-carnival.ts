import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-candy-carnival/manifest').then((module) => adoptPackagedSlotGameManifest(module.CANDY_CARNIVAL_SLOT_GAME))

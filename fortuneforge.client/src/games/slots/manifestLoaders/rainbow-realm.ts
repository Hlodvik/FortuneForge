import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-rainbow-realm/manifest').then((module) => adoptPackagedSlotGameManifest(module.RAINBOW_REALM_SLOT_GAME))

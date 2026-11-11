import { adoptPackagedSlotGameManifest, type SlotGameManifest } from '../shared/slotGameManifest'

export const loadSlotManifest = (): Promise<SlotGameManifest> =>
  import('@fortuneforge/games-robot-revolution/manifest').then((module) => adoptPackagedSlotGameManifest(module.ROBOT_REVOLUTION_SLOT_GAME))

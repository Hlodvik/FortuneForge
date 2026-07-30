import { RAINBOW_REALM_SLOT_GAME } from './rainbowRealm/manifest'
import { createSlotExperienceRouteMap } from './slotGameManifest'
import { WUKONG_SLOT_GAME } from './wukong/manifest'

export const SLOT_GAME_MANIFESTS = [
  WUKONG_SLOT_GAME,
  RAINBOW_REALM_SLOT_GAME,
] as const

export const SLOT_EXPERIENCE_SETS_BY_ROUTE = createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)

export function findSlotGameManifestByRoute(pathname: string) {
  return SLOT_GAME_MANIFESTS.find((manifest) =>
    manifest.routes.play === pathname || manifest.routes.demo === pathname,
  ) ?? null
}

export { RAINBOW_REALM_SLOT_GAME, WUKONG_SLOT_GAME }
export type { SlotGameManifest } from './slotGameManifest'

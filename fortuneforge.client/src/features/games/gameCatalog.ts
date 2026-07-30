import { SLOT_GAME_MANIFESTS } from '../slots/games'
import type { SlotGameCatalogDefinition } from '../slots/games/slotGameManifest'

export type SlotGameCatalogEntry = SlotGameCatalogDefinition & {
  playHref: string | null
  demoHref: string
  serverGameIds: readonly string[]
}

// Routes, cards, and server history lookup are projected from one manifest.
export const SLOT_GAME_CATALOG: readonly SlotGameCatalogEntry[] = SLOT_GAME_MANIFESTS.map(
  (manifest) => ({
    ...manifest.catalog,
    playHref: manifest.routes.play,
    demoHref: manifest.routes.demo,
    serverGameIds: [manifest.experience.rules.gameId],
  }),
)

export const WUKONG_JOURNEY_TO_THE_WEST = SLOT_GAME_CATALOG[0]

export function findSlotGameByServerId(gameId: string): SlotGameCatalogEntry | null {
  return SLOT_GAME_CATALOG.find((game) => game.serverGameIds.includes(gameId)) ?? null
}

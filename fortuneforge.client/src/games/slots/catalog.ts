import { ARCANE_ARCHIVES_CATALOG } from './arcaneArchives/catalog'
import { CANDY_CARNIVAL_DEFINITION } from './candyCarnival/definition'
import { COSMIC_FORTUNE_CATALOG } from './cosmicFortune/catalog'
import { DESERT_TREASURES_DEFINITION } from './desertTreasures/definition'
import { DINO_DOMINION_CATALOG } from './dinoDominion/catalog'
import { DRAGON_HOARD_DEFINITION } from './dragonHoard/definition'
import { GODS_OF_OLYMPUS_CATALOG } from './godsOfOlympus/catalog'
import { HIGH_NOON_FORTUNE_CATALOG } from './highNoonFortune/catalog'
import { JUNGLE_JACKPOT_DEFINITION } from './jungleJackpot/definition'
import { NEON_NIGHTS_DEFINITION } from './neonNights/definition'
import { NORDIC_LEGENDS_DEFINITION } from './nordicLegends/definition'
import { OCEAN_ODYSSEY_DEFINITION } from './oceanOdyssey/definition'
import { PHANTOM_MANOR_DEFINITION } from './phantomManor/definition'
import { PIRATES_FORTUNE_CATALOG } from './piratesFortune/catalog'
import { RAINBOW_REALM_CATALOG } from './rainbowRealm/catalog'
import { REEL_RICHES_CATALOG } from './reelRiches/catalog'
import { ROBOT_REVOLUTION_DEFINITION } from './robotRevolution/definition'
import { ROYAL_DRAW_CATALOG } from './royalDraw/catalog'
import { SAMURAI_FORTUNE_DEFINITION } from './samuraiFortune/definition'
import { SLOT_ROUTE_DEFINITIONS } from './routeRegistry'
import { createShowcaseSlotCatalog } from './shared/createShowcaseSlotCatalog'
import type { SlotGameCatalogDefinition } from './shared/slotGameManifest'
import { WUKONG_CATALOG } from './wukong/catalog'
import type { SlotGameCatalogEntry } from './catalogTypes'

export type { SlotGameCatalogEntry } from './catalogTypes'

const catalogDefinitions: readonly SlotGameCatalogDefinition[] = [
  WUKONG_CATALOG,
  RAINBOW_REALM_CATALOG,
  PIRATES_FORTUNE_CATALOG,
  GODS_OF_OLYMPUS_CATALOG,
  REEL_RICHES_CATALOG,
  HIGH_NOON_FORTUNE_CATALOG,
  ROYAL_DRAW_CATALOG,
  ARCANE_ARCHIVES_CATALOG,
  COSMIC_FORTUNE_CATALOG,
  DINO_DOMINION_CATALOG,
  createShowcaseSlotCatalog(NEON_NIGHTS_DEFINITION),
  createShowcaseSlotCatalog(JUNGLE_JACKPOT_DEFINITION),
  createShowcaseSlotCatalog(OCEAN_ODYSSEY_DEFINITION),
  createShowcaseSlotCatalog(SAMURAI_FORTUNE_DEFINITION),
  createShowcaseSlotCatalog(CANDY_CARNIVAL_DEFINITION),
  createShowcaseSlotCatalog(PHANTOM_MANOR_DEFINITION),
  createShowcaseSlotCatalog(NORDIC_LEGENDS_DEFINITION),
  createShowcaseSlotCatalog(DESERT_TREASURES_DEFINITION),
  createShowcaseSlotCatalog(ROBOT_REVOLUTION_DEFINITION),
  createShowcaseSlotCatalog(DRAGON_HOARD_DEFINITION),
]

const routesById = new Map(SLOT_ROUTE_DEFINITIONS.map((definition) => [definition.id, definition]))

export const SLOT_GAME_CATALOG: readonly SlotGameCatalogEntry[] = catalogDefinitions.map(
  (catalog) => {
    const route = routesById.get(catalog.id)
    if (!route) throw new Error(`Missing slot route metadata for '${catalog.id}'.`)
    return {
      ...catalog,
      playHref: route.playPath,
      demoHref: route.demoPath,
      serverGameIds: route.serverGameIds,
    }
  },
)

export const WUKONG_JOURNEY_TO_THE_WEST = SLOT_GAME_CATALOG[0]

export function findSlotGameByServerId(gameId: string): SlotGameCatalogEntry | null {
  return SLOT_GAME_CATALOG.find((game) => game.serverGameIds.includes(gameId)) ?? null
}

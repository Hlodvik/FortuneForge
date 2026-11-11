import arcaneArchivesTitle from '../../assets/catalogue/slots/arcane-archives-title-v1.webp'
import candyCarnivalTitle from '../../assets/catalogue/slots/candy-carnival-title-v2.webp'
import cosmicFortuneTitle from '../../assets/catalogue/slots/cosmic-fortune-title-v1.webp'
import desertTreasuresTitle from '../../assets/catalogue/slots/desert-treasures-title-v2.webp'
import godsOfOlympusTitle from '../../assets/catalogue/slots/gods-of-olympus-title-v2.webp'
import highNoonFortuneTitle from '../../assets/catalogue/slots/high-noon-fortune-title-v2.webp'
import nordicLegendsTitle from '../../assets/catalogue/slots/nordic-legends-title-v2.webp'
import oceanOdysseyTitle from '../../assets/catalogue/slots/ocean-odyssey-title-v1.webp'
import phantomManorTitle from '../../assets/catalogue/slots/phantom-manor-title-v2.webp'
import piratesFortuneTitle from '../../assets/catalogue/slots/pirates-fortune-title-v2.webp'
import rainbowRealmTitle from '../../assets/catalogue/slots/rainbow-realm-title-v2.webp'
import reelRichesTitle from '../../assets/catalogue/slots/reel-riches-title-v2.webp'
import robotRevolutionTitle from '../../assets/catalogue/slots/robot-revolution-title-v2.webp'
import royalDrawTitle from '../../assets/catalogue/slots/royal-draw-title-v2.webp'
import samuraiFortuneTitle from '../../assets/catalogue/slots/samurai-fortune-title-v1.webp'
import wukongTitle from '../../assets/catalogue/slots/wukong-journey-to-the-west-title-v2.webp'
import { GOLD_FOUNDRY_CATALOG } from './goldFoundry/catalog'
import { MIDNIGHT_VAULT_CATALOG } from './dragonHoard/catalog'
import { JADE_DRAGON_CATALOG } from './jadeDragon/catalog'
import { NEON_NIGHTS_CATALOG } from './neonNights/catalog'
import { SLOT_ROUTE_DEFINITIONS } from './routeRegistry'
import type { SlotGameCatalogDefinition } from './shared/slotGameManifest'
import type { SlotGameCatalogEntry } from './catalogTypes'

export type { SlotGameCatalogEntry } from './catalogTypes'

const hostCatalog = (
  id: string,
  title: string,
  shortTitle: string,
  description: string,
  image: string,
): SlotGameCatalogDefinition => ({ id, title, shortTitle, description, image, imagePresentation: 'cover' })

// Lobby metadata deliberately stays host-owned. Importing this file must not
// pull every standalone slot client, controller, stylesheet, or media bundle.
const catalogDefinitions: readonly SlotGameCatalogDefinition[] = [
  hostCatalog('wukong-journey-to-the-west', "Wukong's Journey to the West", "Wukong's Journey",
    'Ride the nimbus clouds through five celestial reels with Wukong at your side.', wukongTitle),
  hostCatalog('rainbow-realm', 'Rainbow Realm', 'Rainbow Realm',
    'A bright return to the classic fruit symbols, led by the high-value watermelon reel.', rainbowRealmTitle),
  hostCatalog('pirates-fortune', "Pirates' Fortune", "Pirates' Fortune",
    'Sail into a moonlit cove, collect four treasure gems, and let the Doubloon Purse plunder every coin in sight.', piratesFortuneTitle),
  hostCatalog('gods-of-olympus', 'Gods of Olympus', 'Gods of Olympus',
    'Match eight or more symbols on Mount Olympus. When they clear, fresh symbols drop in for another chance to win.', godsOfOlympusTitle),
  hostCatalog('reel-riches', 'Reel Riches', 'Reel Riches',
    'Cast onto a sunrise lake, complete four tackle collections, and sweep pearl prizes into the fishing net.', reelRichesTitle),
  hostCatalog('high-noon-fortune', 'High Noon Fortune', 'High Noon',
    'Ride into a sunset frontier town, complete four badge trails, and lasso every gold nugget on the reels.', highNoonFortuneTitle),
  hostCatalog('royal-draw', 'Royal Draw', 'Royal Draw',
    'Take a seat at the high-stakes table, complete all four suits, and let the dealer tray sweep the jackpot chips.', royalDrawTitle),
  hostCatalog('arcane-archives', 'Arcane Archives', 'Arcane Archives',
    'Open the forbidden stacks, build four seal shelves, and let an enchanted satchel gather raw mana.', arcaneArchivesTitle),
  hostCatalog('cosmic-fortune', 'Cosmic Fortune', 'Cosmic Fortune',
    'Launch through the lucky stars, assemble four planetary orbits, and sweep up dark-matter prizes.', cosmicFortuneTitle),
  GOLD_FOUNDRY_CATALOG,
  NEON_NIGHTS_CATALOG,
  JADE_DRAGON_CATALOG,
  hostCatalog('ocean-odyssey', 'Ocean Odyssey', 'Ocean Odyssey',
    'Descend through coral gardens and ancient currents in search of a luminous pearl kingdom.', oceanOdysseyTitle),
  hostCatalog('samurai-fortune', 'Samurai Fortune', 'Samurai Fortune',
    'Cross a lantern-lit province of blossom gardens, guarded castles, and legendary blades.', samuraiFortuneTitle),
  hostCatalog('candy-carnival', 'Candy Carnival', 'Candy Carnival',
    'Step under the striped big top for spun sugar, candy rides, and a sparkling prize parade.', candyCarnivalTitle),
  hostCatalog('phantom-manor', 'Phantom Manor', 'Phantom Manor',
    'Unlock a moonlit estate of whispering portraits, restless ravens, and spectral treasure.', phantomManorTitle),
  hostCatalog('nordic-legends', 'Nordic Legends', 'Nordic Legends',
    'Sail beneath the aurora toward rune-carved peaks, thunder gods, and a warrior’s hoard.', nordicLegendsTitle),
  hostCatalog('desert-treasures', 'Desert Treasures', 'Desert Treasures',
    'Follow the desert stars past ancient scarabs, hidden oases, and a sealed pharaoh’s vault.', desertTreasuresTitle),
  hostCatalog('robot-revolution', 'Robot Revolution', 'Robot Revolution',
    'Charge a chrome megacity of clever drones, fusion cores, and jackpot-building machines.', robotRevolutionTitle),
  MIDNIGHT_VAULT_CATALOG,
]

const routesById = new Map(SLOT_ROUTE_DEFINITIONS.map((definition) => [definition.id, definition]))

export const SLOT_GAME_CATALOG: readonly SlotGameCatalogEntry[] = catalogDefinitions.map(
  (catalog) => {
    if (catalog.id === JADE_DRAGON_CATALOG.id) return JADE_DRAGON_CATALOG
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

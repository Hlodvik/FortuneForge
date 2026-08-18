import type { SlotGameCatalogEntry } from './catalogTypes'
import { findSlotRoute } from './routeRegistry'
import type { ShowcaseSlotGameDefinition } from './shared/createShowcaseSlotGame'
import type { SlotGameCatalogDefinition } from './shared/slotGameManifest'

type CatalogLoader = () => Promise<SlotGameCatalogDefinition>

const catalogLoaders: Readonly<Record<string, CatalogLoader>> = {
  'wukong-journey-to-the-west': () => import('./wukong/catalog').then((module) => module.WUKONG_CATALOG),
  'rainbow-realm': () => import('./rainbowRealm/catalog').then((module) => module.RAINBOW_REALM_CATALOG),
  'pirates-fortune': () => import('./piratesFortune/catalog').then((module) => module.PIRATES_FORTUNE_CATALOG),
  'gods-of-olympus': () => import('./godsOfOlympus/catalog').then((module) => module.GODS_OF_OLYMPUS_CATALOG),
  'reel-riches': () => import('./reelRiches/catalog').then((module) => module.REEL_RICHES_CATALOG),
  'high-noon-fortune': () => import('./highNoonFortune/catalog').then((module) => module.HIGH_NOON_FORTUNE_CATALOG),
  'royal-draw': () => import('./royalDraw/catalog').then((module) => module.ROYAL_DRAW_CATALOG),
  'arcane-archives': () => import('./arcaneArchives/catalog').then((module) => module.ARCANE_ARCHIVES_CATALOG),
  'cosmic-fortune': () => import('./cosmicFortune/catalog').then((module) => module.COSMIC_FORTUNE_CATALOG),
  'dino-dominion': () => import('./dinoDominion/catalog').then((module) => module.DINO_DOMINION_CATALOG),
  'neon-nights': () => loadShowcaseCatalog(import('./neonNights/definition').then((module) => module.NEON_NIGHTS_DEFINITION)),
  'jungle-jackpot': () => loadShowcaseCatalog(import('./jungleJackpot/definition').then((module) => module.JUNGLE_JACKPOT_DEFINITION)),
  'ocean-odyssey': () => loadShowcaseCatalog(import('./oceanOdyssey/definition').then((module) => module.OCEAN_ODYSSEY_DEFINITION)),
  'samurai-fortune': () => loadShowcaseCatalog(import('./samuraiFortune/definition').then((module) => module.SAMURAI_FORTUNE_DEFINITION)),
  'candy-carnival': () => loadShowcaseCatalog(import('./candyCarnival/definition').then((module) => module.CANDY_CARNIVAL_DEFINITION)),
  'phantom-manor': () => loadShowcaseCatalog(import('./phantomManor/definition').then((module) => module.PHANTOM_MANOR_DEFINITION)),
  'nordic-legends': () => loadShowcaseCatalog(import('./nordicLegends/definition').then((module) => module.NORDIC_LEGENDS_DEFINITION)),
  'desert-treasures': () => loadShowcaseCatalog(import('./desertTreasures/definition').then((module) => module.DESERT_TREASURES_DEFINITION)),
  'robot-revolution': () => loadShowcaseCatalog(import('./robotRevolution/definition').then((module) => module.ROBOT_REVOLUTION_DEFINITION)),
  'dragon-hoard': () => loadShowcaseCatalog(import('./dragonHoard/definition').then((module) => module.DRAGON_HOARD_DEFINITION)),
}

export async function loadSlotGameCatalogById(id: string): Promise<SlotGameCatalogEntry | null> {
  const route = findSlotRouteById(id)
  const loader = catalogLoaders[id]
  if (!route || !loader) return null
  const catalog = await loader()
  return {
    ...catalog,
    playHref: route.playPath,
    demoHref: route.demoPath,
    serverGameIds: route.serverGameIds,
  }
}

async function loadShowcaseCatalog(
  definitionPromise: Promise<ShowcaseSlotGameDefinition>,
): Promise<SlotGameCatalogDefinition> {
  const [definition, { createShowcaseSlotCatalog }] = await Promise.all([
    definitionPromise,
    import('./shared/createShowcaseSlotCatalog'),
  ])
  return createShowcaseSlotCatalog(definition)
}

function findSlotRouteById(id: string) {
  for (const path of [`/slots/${id}`, `/slots/${id}/demo`]) {
    const route = findSlotRoute(path)
    if (route?.id === id) return route
  }
  return id === 'wukong-journey-to-the-west' ? findSlotRoute('/slots/wukong') : null
}

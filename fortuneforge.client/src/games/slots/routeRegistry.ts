import type { SlotGameManifest } from './shared/slotGameManifest'

export type SlotRouteDefinition = Readonly<{
  id: string
  title: string
  shortTitle: string
  playPath: string | null
  demoPath: string
  serverGameIds: readonly string[]
  shellBackdrop: 'default-clouds' | 'theme'
  load: () => Promise<SlotGameManifest>
}>

export const SLOT_ROUTE_DEFINITIONS: readonly SlotRouteDefinition[] = [
  {
    id: 'wukong-journey-to-the-west', title: "Wukong's Journey to the West", shortTitle: "Wukong's Journey",
    playPath: '/slots/wukong', demoPath: '/slots/wukong/demo', serverGameIds: ['classic-demo-v1'], shellBackdrop: 'default-clouds',
    load: () => import('./wukong/manifest').then((module) => module.WUKONG_SLOT_GAME),
  },
  {
    id: 'rainbow-realm', title: 'Rainbow Realm', shortTitle: 'Rainbow Realm',
    playPath: '/slots/rainbow-realm', demoPath: '/slots/rainbow-realm/demo', serverGameIds: ['rainbow-realm-fruits-v1'], shellBackdrop: 'theme',
    load: () => import('./rainbowRealm/manifest').then((module) => module.RAINBOW_REALM_SLOT_GAME),
  },
  {
    id: 'pirates-fortune', title: "Pirates' Fortune", shortTitle: "Pirates' Fortune",
    playPath: '/slots/pirates-fortune', demoPath: '/slots/pirates-fortune/demo', serverGameIds: ['pirates-fortune-v1'], shellBackdrop: 'theme',
    load: () => import('./piratesFortune/manifest').then((module) => module.PIRATES_FORTUNE_SLOT_GAME),
  },
  {
    id: 'gods-of-olympus', title: 'Gods of Olympus', shortTitle: 'Gods of Olympus',
    playPath: '/slots/gods-of-olympus', demoPath: '/slots/gods-of-olympus/demo', serverGameIds: ['gods-of-olympus-v1'], shellBackdrop: 'theme',
    load: () => import('./godsOfOlympus/manifest').then((module) => module.GODS_OF_OLYMPUS_SLOT_GAME),
  },
  {
    id: 'reel-riches', title: 'Reel Riches', shortTitle: 'Reel Riches',
    playPath: '/slots/reel-riches', demoPath: '/slots/reel-riches/demo', serverGameIds: ['reel-riches-v1'], shellBackdrop: 'theme',
    load: () => import('./reelRiches/manifest').then((module) => module.REEL_RICHES_SLOT_GAME),
  },
  {
    id: 'high-noon-fortune', title: 'High Noon Fortune', shortTitle: 'High Noon',
    playPath: '/slots/high-noon-fortune', demoPath: '/slots/high-noon-fortune/demo', serverGameIds: ['high-noon-fortune-v1'], shellBackdrop: 'theme',
    load: () => import('./highNoonFortune/manifest').then((module) => module.HIGH_NOON_FORTUNE_SLOT_GAME),
  },
  {
    id: 'royal-draw', title: 'Royal Draw', shortTitle: 'Royal Draw',
    playPath: '/slots/royal-draw', demoPath: '/slots/royal-draw/demo', serverGameIds: ['royal-draw-v1'], shellBackdrop: 'theme',
    load: () => import('./royalDraw/manifest').then((module) => module.ROYAL_DRAW_SLOT_GAME),
  },
  {
    id: 'arcane-archives', title: 'Arcane Archives', shortTitle: 'Arcane Archives',
    playPath: '/slots/arcane-archives', demoPath: '/slots/arcane-archives/demo', serverGameIds: ['arcane-archives-v1'], shellBackdrop: 'theme',
    load: () => import('./arcaneArchives/manifest').then((module) => module.ARCANE_ARCHIVES_SLOT_GAME),
  },
  {
    id: 'cosmic-fortune', title: 'Cosmic Fortune', shortTitle: 'Cosmic Fortune',
    playPath: '/slots/cosmic-fortune', demoPath: '/slots/cosmic-fortune/demo', serverGameIds: ['cosmic-fortune-v1'], shellBackdrop: 'theme',
    load: () => import('./cosmicFortune/manifest').then((module) => module.COSMIC_FORTUNE_SLOT_GAME),
  },
  {
    id: 'dino-dominion', title: 'Dino Dominion', shortTitle: 'Dino Dominion',
    playPath: '/slots/dino-dominion', demoPath: '/slots/dino-dominion/demo', serverGameIds: ['dino-dominion-v1'], shellBackdrop: 'theme',
    load: () => import('./dinoDominion/manifest').then((module) => module.DINO_DOMINION_SLOT_GAME),
  },
  {
    id: 'neon-nights', title: 'Neon Nights', shortTitle: 'Neon Nights',
    playPath: '/slots/neon-nights', demoPath: '/slots/neon-nights/demo', serverGameIds: ['neon-nights-v1'], shellBackdrop: 'theme',
    load: () => import('./neonNights/manifest').then((module) => module.NEON_NIGHTS_SLOT_GAME),
  },
  {
    id: 'jungle-jackpot', title: 'Jungle Jackpot', shortTitle: 'Jungle Jackpot',
    playPath: '/slots/jungle-jackpot', demoPath: '/slots/jungle-jackpot/demo', serverGameIds: ['jungle-jackpot-v1'], shellBackdrop: 'theme',
    load: () => import('./jungleJackpot/manifest').then((module) => module.JUNGLE_JACKPOT_SLOT_GAME),
  },
  {
    id: 'ocean-odyssey', title: 'Ocean Odyssey', shortTitle: 'Ocean Odyssey',
    playPath: '/slots/ocean-odyssey', demoPath: '/slots/ocean-odyssey/demo', serverGameIds: ['ocean-odyssey-v1'], shellBackdrop: 'theme',
    load: () => import('./oceanOdyssey/manifest').then((module) => module.OCEAN_ODYSSEY_SLOT_GAME),
  },
  {
    id: 'samurai-fortune', title: 'Samurai Fortune', shortTitle: 'Samurai Fortune',
    playPath: '/slots/samurai-fortune', demoPath: '/slots/samurai-fortune/demo', serverGameIds: ['samurai-fortune-v1'], shellBackdrop: 'theme',
    load: () => import('./samuraiFortune/manifest').then((module) => module.SAMURAI_FORTUNE_SLOT_GAME),
  },
  {
    id: 'candy-carnival', title: 'Candy Carnival', shortTitle: 'Candy Carnival',
    playPath: '/slots/candy-carnival', demoPath: '/slots/candy-carnival/demo', serverGameIds: ['candy-carnival-v1'], shellBackdrop: 'theme',
    load: () => import('./candyCarnival/manifest').then((module) => module.CANDY_CARNIVAL_SLOT_GAME),
  },
  {
    id: 'phantom-manor', title: 'Phantom Manor', shortTitle: 'Phantom Manor',
    playPath: '/slots/phantom-manor', demoPath: '/slots/phantom-manor/demo', serverGameIds: ['phantom-manor-v1'], shellBackdrop: 'theme',
    load: () => import('./phantomManor/manifest').then((module) => module.PHANTOM_MANOR_SLOT_GAME),
  },
  {
    id: 'nordic-legends', title: 'Nordic Legends', shortTitle: 'Nordic Legends',
    playPath: '/slots/nordic-legends', demoPath: '/slots/nordic-legends/demo', serverGameIds: ['nordic-legends-v1'], shellBackdrop: 'theme',
    load: () => import('./nordicLegends/manifest').then((module) => module.NORDIC_LEGENDS_SLOT_GAME),
  },
  {
    id: 'desert-treasures', title: 'Desert Treasures', shortTitle: 'Desert Treasures',
    playPath: '/slots/desert-treasures', demoPath: '/slots/desert-treasures/demo', serverGameIds: ['desert-treasures-v1'], shellBackdrop: 'theme',
    load: () => import('./desertTreasures/manifest').then((module) => module.DESERT_TREASURES_SLOT_GAME),
  },
  {
    id: 'robot-revolution', title: 'Robot Revolution', shortTitle: 'Robot Revolution',
    playPath: '/slots/robot-revolution', demoPath: '/slots/robot-revolution/demo', serverGameIds: ['robot-revolution-v1'], shellBackdrop: 'theme',
    load: () => import('./robotRevolution/manifest').then((module) => module.ROBOT_REVOLUTION_SLOT_GAME),
  },
  {
    id: 'dragon-hoard', title: 'Dragon Hoard', shortTitle: 'Dragon Hoard',
    playPath: '/slots/dragon-hoard', demoPath: '/slots/dragon-hoard/demo', serverGameIds: ['dragon-hoard-v1'], shellBackdrop: 'theme',
    load: () => import('./dragonHoard/manifest').then((module) => module.DRAGON_HOARD_SLOT_GAME),
  },
]

export const SECOND_WAVE_SLOT_GAME_IDS = [
  'neon-nights', 'jungle-jackpot', 'ocean-odyssey', 'samurai-fortune', 'candy-carnival',
  'phantom-manor', 'nordic-legends', 'desert-treasures', 'robot-revolution', 'dragon-hoard',
] as const

const routes = new Map<string, SlotRouteDefinition>()
for (const definition of SLOT_ROUTE_DEFINITIONS) {
  for (const path of [definition.playPath, definition.demoPath]) {
    if (path === null) continue
    if (routes.has(path)) throw new Error(`Duplicate slot route '${path}'.`)
    routes.set(path, definition)
  }
}

export function findSlotRoute(pathname: string): SlotRouteDefinition | null {
  return routes.get(pathname) ?? null
}

export function findSlotRouteByServerId(gameId: string): SlotRouteDefinition | null {
  return SLOT_ROUTE_DEFINITIONS.find((definition) => definition.serverGameIds.includes(gameId)) ?? null
}

export async function loadSlotGameByRoute(pathname: string): Promise<SlotGameManifest | null> {
  const definition = findSlotRoute(pathname)
  return definition === null ? null : definition.load()
}

export function loadAllSlotGameManifests(): Promise<readonly SlotGameManifest[]> {
  return Promise.all(SLOT_ROUTE_DEFINITIONS.map((definition) => definition.load()))
}

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

const slotRouteMetadata = [
  {
    id: 'wukong-journey-to-the-west', title: "Wukong's Journey to the West", shortTitle: "Wukong's Journey",
    playPath: '/slots/wukong', demoPath: '/slots/wukong/demo', serverGameIds: ['wukong-journey-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'rainbow-realm', title: 'Rainbow Realm', shortTitle: 'Rainbow Realm',
    playPath: '/slots/rainbow-realm', demoPath: '/slots/rainbow-realm/demo', serverGameIds: ['rainbow-realm-fruits-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'pirates-fortune', title: "Pirates' Fortune", shortTitle: "Pirates' Fortune",
    playPath: '/slots/pirates-fortune', demoPath: '/slots/pirates-fortune/demo', serverGameIds: ['pirates-fortune-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'gods-of-olympus', title: 'Gods of Olympus', shortTitle: 'Gods of Olympus',
    playPath: '/slots/gods-of-olympus', demoPath: '/slots/gods-of-olympus/demo', serverGameIds: ['gods-of-olympus-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'reel-riches', title: 'Reel Riches', shortTitle: 'Reel Riches',
    playPath: '/slots/reel-riches', demoPath: '/slots/reel-riches/demo', serverGameIds: ['reel-riches-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'high-noon-fortune', title: 'High Noon Fortune', shortTitle: 'High Noon',
    playPath: '/slots/high-noon-fortune', demoPath: '/slots/high-noon-fortune/demo', serverGameIds: ['high-noon-fortune-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'royal-draw', title: 'Royal Draw', shortTitle: 'Royal Draw',
    playPath: '/slots/royal-draw', demoPath: '/slots/royal-draw/demo', serverGameIds: ['royal-draw-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'arcane-archives', title: 'Arcane Archives', shortTitle: 'Arcane Archives',
    playPath: '/slots/arcane-archives', demoPath: '/slots/arcane-archives/demo', serverGameIds: ['arcane-archives-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'cosmic-fortune', title: 'Cosmic Fortune', shortTitle: 'Cosmic Fortune',
    playPath: '/slots/cosmic-fortune', demoPath: '/slots/cosmic-fortune/demo', serverGameIds: ['cosmic-fortune-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'gold-foundry', title: 'Gold Foundry', shortTitle: 'Gold Foundry',
    playPath: '/slots/gold-foundry', demoPath: '/slots/gold-foundry/demo', serverGameIds: ['gold-foundry-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'neon-nights', title: 'Neon Nights', shortTitle: 'Neon Nights',
    playPath: '/slots/neon-nights', demoPath: '/slots/neon-nights/demo', serverGameIds: ['neon-nights-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'ocean-odyssey', title: 'Ocean Odyssey', shortTitle: 'Ocean Odyssey',
    playPath: '/slots/ocean-odyssey', demoPath: '/slots/ocean-odyssey/demo', serverGameIds: ['ocean-odyssey-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'samurai-fortune', title: 'Samurai Fortune', shortTitle: 'Samurai Fortune',
    playPath: '/slots/samurai-fortune', demoPath: '/slots/samurai-fortune/demo', serverGameIds: ['samurai-fortune-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'candy-carnival', title: 'Candy Carnival', shortTitle: 'Candy Carnival',
    playPath: '/slots/candy-carnival', demoPath: '/slots/candy-carnival/demo', serverGameIds: ['candy-carnival-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'phantom-manor', title: 'Phantom Manor', shortTitle: 'Phantom Manor',
    playPath: '/slots/phantom-manor', demoPath: '/slots/phantom-manor/demo', serverGameIds: ['phantom-manor-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'nordic-legends', title: 'Nordic Legends', shortTitle: 'Nordic Legends',
    playPath: '/slots/nordic-legends', demoPath: '/slots/nordic-legends/demo', serverGameIds: ['nordic-legends-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'desert-treasures', title: 'Desert Treasures', shortTitle: 'Desert Treasures',
    playPath: '/slots/desert-treasures', demoPath: '/slots/desert-treasures/demo', serverGameIds: ['desert-treasures-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'robot-revolution', title: 'Robot Revolution', shortTitle: 'Robot Revolution',
    playPath: '/slots/robot-revolution', demoPath: '/slots/robot-revolution/demo', serverGameIds: ['robot-revolution-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'dragon-hoard', title: 'Midnight Vault', shortTitle: 'Midnight Vault',
    playPath: '/slots/dragon-hoard', demoPath: '/slots/dragon-hoard/demo', serverGameIds: ['dragon-hoard-v1'], shellBackdrop: 'theme',
  },
] as const satisfies readonly Omit<SlotRouteDefinition, 'load'>[]

export type SlotRouteId = typeof slotRouteMetadata[number]['id']

type SlotManifestLoaderModule = Readonly<{
  loadSlotManifest: SlotRouteDefinition['load']
}>

// Keep one local module between the route registry and every game package. In
// development Vite only resolves the selected game's package when its route is
// opened, so a missing sibling cannot break application startup or another game.
const slotManifestLoaderModules: Readonly<Record<SlotRouteId, () => Promise<SlotManifestLoaderModule>>> = {
  'wukong-journey-to-the-west': () => import('./manifestLoaders/wukong-journey-to-the-west'),
  'rainbow-realm': () => import('./manifestLoaders/rainbow-realm'),
  'pirates-fortune': () => import('./manifestLoaders/pirates-fortune'),
  'gods-of-olympus': () => import('./manifestLoaders/gods-of-olympus'),
  'reel-riches': () => import('./manifestLoaders/reel-riches'),
  'high-noon-fortune': () => import('./manifestLoaders/high-noon-fortune'),
  'royal-draw': () => import('./manifestLoaders/royal-draw'),
  'arcane-archives': () => import('./manifestLoaders/arcane-archives'),
  'cosmic-fortune': () => import('./manifestLoaders/cosmic-fortune'),
  'gold-foundry': () => import('./manifestLoaders/gold-foundry'),
  'neon-nights': () => import('./manifestLoaders/neon-nights'),
  'ocean-odyssey': () => import('./manifestLoaders/ocean-odyssey'),
  'samurai-fortune': () => import('./manifestLoaders/samurai-fortune'),
  'candy-carnival': () => import('./manifestLoaders/candy-carnival'),
  'phantom-manor': () => import('./manifestLoaders/phantom-manor'),
  'nordic-legends': () => import('./manifestLoaders/nordic-legends'),
  'desert-treasures': () => import('./manifestLoaders/desert-treasures'),
  'robot-revolution': () => import('./manifestLoaders/robot-revolution'),
  'dragon-hoard': () => import('./manifestLoaders/dragon-hoard'),
}

// Standalone hosts use the route metadata without resolving other game packages.
export const SLOT_ROUTE_DEFINITIONS: readonly SlotRouteDefinition[] = slotRouteMetadata.map((route) => ({
  ...route,
  load: () => slotManifestLoaderModules[route.id]().then((module) => module.loadSlotManifest()),
}))

export const SECOND_WAVE_SLOT_GAME_IDS = [
  'neon-nights', 'ocean-odyssey', 'samurai-fortune', 'candy-carnival',
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

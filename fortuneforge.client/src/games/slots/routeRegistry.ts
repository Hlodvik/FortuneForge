// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import type { SlotGameManifest } from './shared/slotGameManifest'
import { SLOT_ROUTE_METADATA, type SlotRouteMetadata, type SlotRouteId } from './routeMetadata'
export type { SlotRouteId } from './routeMetadata'

export type SlotRouteDefinition = SlotRouteMetadata & Readonly<{ load: () => Promise<SlotGameManifest> }>

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
  'pharaohs-ascent': () => import('./manifestLoaders/pharaohs-ascent'),
  'gold-foundry': () => import('./manifestLoaders/gold-foundry'),
  'neon-nights': () => import('./manifestLoaders/neon-nights'),
  'ocean-odyssey': () => import('./manifestLoaders/ocean-odyssey'),
  'crooked-road': () => import('./manifestLoaders/crooked-road'),
  'candy-carnival': () => import('./manifestLoaders/candy-carnival'),
  'phantom-manor': () => import('./manifestLoaders/phantom-manor'),
  'nordic-legends': () => import('./manifestLoaders/nordic-legends'),
  'desert-treasures': () => import('./manifestLoaders/desert-treasures'),
  'robot-revolution': () => import('./manifestLoaders/robot-revolution'),
  'dragon-hoard': () => import('./manifestLoaders/dragon-hoard'),
}

// Gameplay adds lazy loaders to the same metadata used independently by the lobby.
export const SLOT_ROUTE_DEFINITIONS: readonly SlotRouteDefinition[] = SLOT_ROUTE_METADATA.map((route) => ({
  ...route,
  load: () => slotManifestLoaderModules[route.id]().then((module) => module.loadSlotManifest()),
}))

export const SECOND_WAVE_SLOT_GAME_IDS = [
  'neon-nights', 'ocean-odyssey', 'crooked-road', 'candy-carnival',
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

// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
export type SlotRouteMetadata = Readonly<{
  id: string
  title: string
  shortTitle: string
  gameReleaseId?: string
  playPath: string | null
  demoPath: string | null
  serverGameIds: readonly string[]
  shellBackdrop: 'default-clouds' | 'theme'
  legacyPaths?: Readonly<Record<string, string>>
}>

export const SLOT_ROUTE_METADATA = [
  {
    id: 'wukong-journey-to-the-west', gameReleaseId: 'wukong', title: "Wukong's Journey to the West", shortTitle: "Wukong's Journey",
    playPath: '/slots/wukong', demoPath: '/slots/wukong/demo', serverGameIds: ['wukong-journey-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'rainbow-realm', title: 'Rainbow Realm', shortTitle: 'Rainbow Realm',
    playPath: '/slots/rainbow-realm', demoPath: '/slots/rainbow-realm/demo', serverGameIds: ['rainbow-realm-fruits-v1'], shellBackdrop: 'theme',
  },
  {
    id: 'pirates-fortune', title: "Treasure Cove", shortTitle: "Treasure Cove",
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
    id: 'high-noon-fortune', title: 'Frontier Fortune', shortTitle: 'Frontier Fortune',
    playPath: '/slots/frontier-fortune', demoPath: null, serverGameIds: ['high-noon-fortune-v1'], shellBackdrop: 'theme',
    legacyPaths: { '/slots/high-noon-fortune': '/slots/frontier-fortune', '/slots/high-noon-fortune/demo': '/slots/frontier-fortune' },
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
    id: 'cosmic-fortune', title: 'Pharaoh’s Ascent', shortTitle: 'Pharaoh’s Ascent',
    playPath: '/slots/pharaohs-ascent', demoPath: null, serverGameIds: ['cosmic-fortune-v1'], shellBackdrop: 'theme',
    legacyPaths: { '/slots/cosmic-fortune': '/slots/pharaohs-ascent', '/slots/cosmic-fortune/demo': '/slots/pharaohs-ascent' },
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
    id: 'crooked-road', title: 'The Crooked Road', shortTitle: 'The Crooked Road',
    playPath: '/slots/crooked-road', demoPath: null, serverGameIds: ['crooked-road-v1'], shellBackdrop: 'theme',
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
    id: 'robot-revolution', title: 'Sunstone Eclipse', shortTitle: 'Sunstone Eclipse',
    playPath: '/slots/sunstone-eclipse', demoPath: '/slots/sunstone-eclipse/demo', serverGameIds: ['robot-revolution-v1'], shellBackdrop: 'theme',
    legacyPaths: { '/slots/robot-revolution': '/slots/sunstone-eclipse', '/slots/robot-revolution/demo': '/slots/sunstone-eclipse/demo' },
  },
  {
    id: 'dragon-hoard', title: 'Midnight Vault', shortTitle: 'Midnight Vault',
    playPath: '/slots/dragon-hoard', demoPath: '/slots/dragon-hoard/demo', serverGameIds: ['dragon-hoard-v1'], shellBackdrop: 'theme',
  },
] as const satisfies readonly SlotRouteMetadata[]

export type SlotRouteId = typeof SLOT_ROUTE_METADATA[number]['id']

const routes = new Map<string, SlotRouteMetadata>()
for (const definition of SLOT_ROUTE_METADATA) {
  for (const path of [definition.playPath, definition.demoPath]) {
    if (path === null) continue
    if (routes.has(path)) throw new Error(`Duplicate slot route '${path}'.`)
    routes.set(path, definition)
  }
}

export function findSlotRouteMetadata(pathname: string): SlotRouteMetadata | null {
  return routes.get(pathname) ?? null
}

export function findSlotRouteMetadataByServerId(gameId: string): SlotRouteMetadata | null {
  return SLOT_ROUTE_METADATA.find((definition) => (definition.serverGameIds as readonly string[]).includes(gameId)) ?? null
}

/** Rethemes keep old bookmarks while publishing only the current game route. */
export function canonicalSlotPath(pathname: string): string {
  for (const definition of SLOT_ROUTE_METADATA) {
    if ('legacyPaths' in definition && Object.hasOwn(definition.legacyPaths, pathname)) {
      return definition.legacyPaths[pathname as keyof typeof definition.legacyPaths]
    }
  }
  return pathname
}

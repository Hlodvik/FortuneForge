import type { SlotCabinetTheme, SlotSymbolSet } from '@fortuneforge/slot-client-core'
import { CROOKED_ROAD_DEFINITION as definition, CROOKED_ROAD_RULE_CONTRACT as rules } from './definition'
import { CROOKED_ROAD_CATALOG } from './catalog'
import { CROOKED_ROAD_SOUNDS } from './sounds'
import { CrookedRoadHelp } from './CrookedRoadHelp'

export const crookedRoadRules = {
  gameId: definition.serverGameId,
  pointValueInCents: rules.pointValueInCents,
  initialReels: definition.initialReels,
  wagerOptions: Array.from({ length: (rules.maximumWagerPoints - rules.minimumWagerPoints) / rules.wagerIncrementPoints + 1 },
    (_, index) => rules.minimumWagerPoints + index * rules.wagerIncrementPoints),
  defaultWagerRand: 3,
  autoSpinDelayMs: 1000, autoSpinSpeedMultiplier: 3, baseSpinSpeedMultiplier: 1,
  reelTravelRowCount: 8, specialBoostCost: 0, energyMeterCapacity: 0,
}

export const crookedRoadSymbols: SlotSymbolSet = {
  id: definition.serverSymbolSetId, serverSymbolSetId: definition.serverSymbolSetId,
  definitions: Object.fromEntries(rules.symbols.map(({ id }) => {
    const [label, glyph, image] = definition.symbolSpecs[id as keyof typeof definition.symbolSpecs]
    return [id, { id, label, glyph, image }]
  })),
  animationSymbolIds: rules.symbols.flatMap(({ id, weight }) => Array.from({ length: weight }, () => id)),
  guideEntries: definition.symbolGuide.entries,
  guideNote: definition.symbolGuide.note,
}

export const crookedRoadCabinet: SlotCabinetTheme = {
  id: 'crooked-road-cabinet-v1', chrome: 'simple', controlLayout: 'sidecar',
  accessibleName: 'The Crooked Road themed slot machine',
  eyebrow: 'Fortune Forge presents', title: definition.title, subtitle: definition.subtitle,
  titleArtwork: definition.artwork.title, emblemImage: definition.artwork.emblem,
  accentImage: definition.artwork.accent, celebrationEffect: definition.celebrationEffect,
  backdropImage: definition.artwork.backdrop, visualsBackdropImage: definition.artwork.backdrop,
  pageBackdropImage: definition.artwork.backdrop, paylineTrace: definition.paylineTrace,
  palette: { shellTop: '#173f31', shellBottom: '#061d15', panel: '#061d15', trim: '#bc9652',
    trimBright: '#e9c976', accent: '#82b88a', glow: '#b7db8f', text: '#fff1ce' },
}

/** Catalogue compatibility data only. Both browser entries execute CrookedRoadGame. */
export const CROOKED_ROAD_SLOT_GAME = {
  id: definition.id,
  routes: { play: '/slots/crooked-road', demo: null },
  catalog: CROOKED_ROAD_CATALOG,
  experience: {
    id: 'crooked-road-experience-v1', cabinet: crookedRoadCabinet,
    shellBackdrop: 'theme' as const, mascot: null, features: {},
    symbols: crookedRoadSymbols, sounds: CROOKED_ROAD_SOUNDS, rules: crookedRoadRules,
    outcomeNarrative: definition.outcomeNarrative,
    help: { paylineCount: rules.paylines.length, paylinePatternIds: definition.paylinePatternIds },
    presentation: {
      winPresentation: 'payline-popups' as const, symbolValueGuide: { mode: 'none' as const },
      winHelp: { Content: CrookedRoadHelp, title: 'A guide to The Crooked Road',
        eyebrow: 'Reels, roads and Otherworld journeys', theme: 'crooked-road' },
    },
  },
}

import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { COSMIC_FORTUNE_CABINET_THEME } from './cabinetTheme'
import { COSMIC_FORTUNE_CATALOG } from './catalog'
import { COSMIC_FORTUNE_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Plasma charge', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Planetary orbit collections',
    itemLabel: 'worlds',
    presentation: 'star-orbit',
    entries: [
      { id: 'sync', label: 'Binary link', shortLabel: 'Binary', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Orbital expansion', shortLabel: 'Orbit', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Tractor beam', shortLabel: 'Beam', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Star map', shortLabel: 'Map', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The tractor-beam saucer',
    awardLabel: 'Dark-matter haul',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 15,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 17, 18, 19, 20, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'BEAM',
      title: 'Dark-matter haul',
      body: 'The tractor-beam saucer gathers every dark-matter crystal in view. Two saucers double the haul. Three meteor showers in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'ORBIT',
      title: 'Planetary collections',
      body: 'Collect 40 worlds in any orbit to unlock ten special free spins. Plasma charge improves planet odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest orbit.',
    },
  ],
}

export const COSMIC_FORTUNE_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'cosmic-fortune-experience-v1',
  cabinet: COSMIC_FORTUNE_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  shellBackdrop: 'theme',
  symbols: COSMIC_FORTUNE_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('cosmic-fortune-v1'),
}

export const COSMIC_FORTUNE_SLOT_GAME = defineSlotGame({
  id: 'cosmic-fortune',
  routes: { play: '/slots/cosmic-fortune', demo: '/slots/cosmic-fortune/demo' },
  catalog: COSMIC_FORTUNE_CATALOG,
  experience: COSMIC_FORTUNE_EXPERIENCE_SET,
})

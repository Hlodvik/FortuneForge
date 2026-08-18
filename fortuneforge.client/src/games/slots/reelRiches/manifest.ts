import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { REEL_RICHES_CABINET_THEME } from './cabinetTheme'
import { REEL_RICHES_CATALOG } from './catalog'
import { REEL_RICHES_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Tide charge', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Tackle badge collections',
    itemLabel: 'badges',
    presentation: 'tackle-creel',
    entries: [
      { id: 'sync', label: 'Perfect cast', shortLabel: 'Lure', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Rising tide', shortLabel: 'Wave', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Net frenzy', shortLabel: 'Net', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Jackpot hook', shortLabel: 'Hook', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The fishing net',
    awardLabel: 'Net haul',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 19,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'NET',
      title: 'Net haul',
      body: 'A fishing net catches every pearl token showing in the window. Two nets double the haul. Three golden fish schools in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'TACKLE',
      title: 'Legendary tackle collections',
      body: 'Collect 40 lure, wave, net, or hook badges to unlock ten special free spins. Tide charge improves badge odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest tackle track.',
    },
  ],
}

export const REEL_RICHES_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'reel-riches-experience-v1',
  cabinet: REEL_RICHES_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  shellBackdrop: 'theme',
  symbols: REEL_RICHES_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('reel-riches-v1'),
}

export const REEL_RICHES_SLOT_GAME = defineSlotGame({
  id: 'reel-riches',
  routes: { play: '/slots/reel-riches', demo: '/slots/reel-riches/demo' },
  catalog: REEL_RICHES_CATALOG,
  experience: REEL_RICHES_EXPERIENCE_SET,
})

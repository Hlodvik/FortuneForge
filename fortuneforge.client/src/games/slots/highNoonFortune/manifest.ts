import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { HIGH_NOON_FORTUNE_CABINET_THEME } from './cabinetTheme'
import { HIGH_NOON_FORTUNE_CATALOG } from './catalog'
import { HIGH_NOON_FORTUNE_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Fuse charge', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Frontier badge collections',
    itemLabel: 'badges',
    presentation: 'frontier-trail',
    entries: [
      { id: 'sync', label: 'Quick draw', shortLabel: 'Bandana', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Canyon trail', shortLabel: 'Spur', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Lasso rush', shortLabel: 'Lasso', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Gold trail', shortLabel: 'Cactus', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The golden lasso',
    awardLabel: 'Lasso roundup',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 18,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 20, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'LASSO',
      title: 'Lasso roundup',
      body: 'The golden lasso ropes every gold-nugget value showing in the window. Two lassos double the roundup. Three dynamite bundles in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'BADGES',
      title: 'Frontier badge collections',
      body: 'Collect 40 bandana, spur, lasso, or cactus badges to unlock ten special free spins. Fuse charge improves badge odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest frontier track.',
    },
  ],
}

export const HIGH_NOON_FORTUNE_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'high-noon-fortune-experience-v1',
  cabinet: HIGH_NOON_FORTUNE_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  shellBackdrop: 'theme',
  symbols: HIGH_NOON_FORTUNE_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('high-noon-fortune-v1'),
}

export const HIGH_NOON_FORTUNE_SLOT_GAME = defineSlotGame({
  id: 'high-noon-fortune',
  routes: { play: '/slots/high-noon-fortune', demo: '/slots/high-noon-fortune/demo' },
  catalog: HIGH_NOON_FORTUNE_CATALOG,
  experience: HIGH_NOON_FORTUNE_EXPERIENCE_SET,
})

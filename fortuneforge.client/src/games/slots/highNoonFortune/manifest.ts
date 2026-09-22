import { HIGH_NOON_FORTUNE_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { HIGH_NOON_FORTUNE_CABINET_THEME } from './cabinetTheme'
import { HIGH_NOON_FORTUNE_CATALOG } from './catalog'
import { HIGH_NOON_FORTUNE_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  moneyGrab: {
    actorName: 'The golden lasso',
    awardLabel: 'Lasso roundup',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
  specialRound: {
    id: 'high-noon-showdown', title: 'Showdown Spin',
    earnLabel: 'Land 3 saloon doors for 5 Showdown Spins.',
    earnStyle: 'gates', earnHint: 'Three saloon doors in one spin opens a Showdown with a linked matching reel.',
    activeModes: {
      sync: 'Quick Draw · a matching reel is copied', rows: 'Canyon Trail · two extra rows',
      paw: 'Lasso Rush · stronger gold collection', rand: 'Gold Trail · a prize column appears',
    },
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
      badge: 'DOORS',
      title: 'Saloon-door trigger',
      body: 'Land three saloon doors anywhere in one spin to unlock five enhanced Showdown Spins. Every Showdown uses a linked matching reel.',
    },
  ],
}

export const HIGH_NOON_FORTUNE_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'high-noon-fortune-experience-v1',
  cabinet: HIGH_NOON_FORTUNE_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  outcomeNarrative: {
    lossTitle: 'No gold struck this spin',
    winTitle: 'Gold strike',
    greatWinTitle: 'Big strike',
    bigWinTitle: 'Frontier jackpot',
    freeGameSingular: 'free frontier spin',
    freeGamePlural: 'free frontier spins',
    lossNextAction: 'Choose a wager, then ride the frontier again.',
    winNextAction: 'Choose a wager, then chase another gold strike.',
    bonusNextAction: 'Your next frontier spin is free and uses the locked wager.',
  },
  shellBackdrop: 'theme',
  symbols: HIGH_NOON_FORTUNE_SYMBOLS,
  mascot: null,
  sounds: HIGH_NOON_FORTUNE_SOUNDS,
  rules: createSlotRulesSet('high-noon-fortune-v1'),
}

export const HIGH_NOON_FORTUNE_SLOT_GAME = defineSlotGame({
  id: 'high-noon-fortune',
  routes: { play: '/slots/high-noon-fortune', demo: '/slots/high-noon-fortune/demo' },
  catalog: HIGH_NOON_FORTUNE_CATALOG,
  experience: HIGH_NOON_FORTUNE_EXPERIENCE_SET,
})

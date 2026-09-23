import { PIRATES_FORTUNE_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { PIRATES_FORTUNE_CABINET_THEME } from './cabinetTheme'
import { PIRATES_FORTUNE_CATALOG } from './catalog'
import { PIRATES_FORTUNE_CHEST_PROGRESSIONS } from './chestProgressions'
import { PIRATES_FORTUNE_SYMBOLS } from './symbols'

const { emerald, lapis, ruby, topaz } = PIRATES_FORTUNE_CHEST_PROGRESSIONS

const PIRATES_FORTUNE_FEATURES: SlotFeatureSet = {
  collections: {
    ariaLabel: 'Treasure gem collections',
    itemLabel: 'gems',
    presentation: 'gem-hoard',
    completionDialog: true,
    entries: [
      {
        id: 'sync', label: 'Matching reel', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 15,
        rewardDescription: 'Fill this chest to launch 10 free games. During every free game, one reel is copied to match the winning setup.',
        containerImage: ruby.empty, containerFillImages: ruby.fillLevels,
      },
      {
        id: 'rows', label: 'Extra rows', shortLabel: 'Lapis', symbol: 'SEAL_ROWS', requiredCount: 15,
        rewardDescription: 'Fill this chest to launch 10 free games. During every free game, two extra reel rows open for more winning ways.',
        containerImage: lapis.empty, containerFillImages: lapis.fillLevels,
      },
      {
        id: 'paw', label: 'Stronger purse hauls', shortLabel: 'Orange', symbol: 'SEAL_PAW', requiredCount: 15,
        rewardDescription: 'Fill this chest to launch 10 free games. During every free game, Doubloon Purse hauls become stronger.',
        containerImage: topaz.empty, containerFillImages: topaz.fillLevels,
      },
      {
        id: 'rand', label: 'Prize multiplier column', shortLabel: 'Emerald', symbol: 'SEAL_RAND', requiredCount: 15,
        rewardDescription: 'Fill this chest to launch 10 free games. During every free game, a prize multiplier column appears.',
        containerImage: emerald.empty, containerFillImages: emerald.fillLevels,
      },
    ],
  },
  moneyGrab: {
    actorName: 'The Doubloon Purse',
    awardLabel: 'Doubloon haul',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
  specialRound: {
    id: 'pirates-broadside', title: 'Free Game',
    earnLabel: 'Land 3 Treasure Maps for 7 Island Search bonus rounds, or complete 15 matching gems for 10 free games.',
    earnStyle: 'buckets', earnHint: 'Ruby, lapis, orange, and emerald gems tumble into four treasure chests. Fill one chest to launch 10 free games with that chest\'s special feature.',
    activeModes: {
      sync: 'Matching Reel · one reel is copied to match the winning setup', rows: 'Extra Rows · two extra reel rows open',
      paw: 'Stronger Purse Hauls · Doubloon Purse hauls are boosted', rand: 'Prize Multiplier Column · a prize multiplier column appears',
    },
  },
}

const PIRATES_FORTUNE_HELP: SlotHelpDefinition = {
  paylineCount: 21,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 23],
  freeGames: {
    requiredSymbols: 3,
    awardedSpins: 7,
    title: 'Island Search bonus',
    symbolLabel: 'TREASURE MAP',
    awardLabel: 'Island Search bonus rounds',
  },
  extraSections: [
    {
      badge: 'PURSE',
      title: 'Doubloon purse haul',
      body: 'A doubloon purse anywhere on screen collects every doubloon multiplier showing in the window. Two purses are much rarer and double the haul. Three powder-keg stacks in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'GEMS',
      title: 'Treasure gem collections',
      body: 'Ruby, lapis, orange, and emerald gems collect from anywhere visible. A completed 15-gem chest awards 10 free games with that chest\'s special feature, tied to the collection\'s average wager.',
    },
  ],
}

export const PIRATES_FORTUNE_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'pirates-fortune-experience-v1',
  cabinet: PIRATES_FORTUNE_CABINET_THEME,
  features: PIRATES_FORTUNE_FEATURES,
  help: PIRATES_FORTUNE_HELP,
  shellBackdrop: 'theme',
  symbols: PIRATES_FORTUNE_SYMBOLS,
  mascot: null,
  sounds: PIRATES_FORTUNE_SOUNDS,
  rules: createSlotRulesSet('pirates-fortune-v1', {
    initialReels: [
      ['2', '3', '4', '6'],
      ['3', '4', '5', '6'],
      ['4', '5', '6', '7'],
      ['5', '6', '7', 'ACE'],
      ['FREE', '7', 'ACE', 'POWER'],
    ],
  }),
}

export const PIRATES_FORTUNE_SLOT_GAME = defineSlotGame({
  id: 'pirates-fortune',
  routes: {
    play: '/slots/pirates-fortune',
    demo: '/slots/pirates-fortune/demo',
  },
  catalog: PIRATES_FORTUNE_CATALOG,
  experience: PIRATES_FORTUNE_EXPERIENCE_SET,
})

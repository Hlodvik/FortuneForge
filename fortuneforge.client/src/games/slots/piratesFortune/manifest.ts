import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { PIRATES_FORTUNE_CABINET_THEME } from './cabinetTheme'
import { PIRATES_FORTUNE_CATALOG } from './catalog'
import { PIRATES_FORTUNE_SYMBOLS } from './symbols'

const PIRATES_FORTUNE_FEATURES: SlotFeatureSet = {
  energy: {
    label: 'Storm charge',
    symbol: 'BOLT',
  },
  collections: {
    ariaLabel: 'Treasure gem collections',
    itemLabel: 'gems',
    presentation: 'gem-hoard',
    entries: [
      { id: 'sync', label: 'Broadside sync', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'High-tide rows', shortLabel: 'Sapphire', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Skull storm', shortLabel: 'Amber', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Doubloon column', shortLabel: 'Emerald', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The Jolly Roger',
    awardLabel: 'Skull plunder',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const PIRATES_FORTUNE_HELP: SlotHelpDefinition = {
  paylineCount: 21,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 23],
  freeGames: {
    requiredSymbols: 3,
    awardedSpins: 5,
  },
  extraSections: [
    {
      badge: 'SKULL',
      title: 'Skull-and-crossbones plunder',
      body: 'A skull-and-crossbones anywhere on screen plunders every doubloon multiplier showing in the window. Two skulls are much rarer and double the haul. Three powder-keg stacks in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'GEMS',
      title: 'Treasure gem collections',
      body: 'Ruby, sapphire, amber, and emerald gems collect from anywhere visible. A completed 40-gem collection awards ten free spins tied to that collection\'s average wager. Storm charge at 25%, 50%, and 75% improves gem odds; a full meter boosts the payout by 1.5×, resets, and finishes the nearest gem track.',
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
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('pirates-fortune-v1'),
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

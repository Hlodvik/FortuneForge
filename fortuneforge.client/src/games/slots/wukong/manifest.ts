import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { WUKONG_CABINET_THEME } from './cabinetTheme'
import { WUKONG_CATALOG } from './catalog'
import { WUKONG_SYMBOLS } from './symbols'

const WUKONG_FEATURES: SlotFeatureSet = {
  energy: {
    label: 'Energy',
    symbol: 'BOLT',
  },
  collections: {
    ariaLabel: 'Power seal collections',
    presentation: 'celestial-orbit',
    entries: [
      { id: 'sync', label: 'Synced reels', shortLabel: 'Sync', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Extra rows', shortLabel: '+2 rows', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Monkey paw', shortLabel: 'Paws', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Rand column', shortLabel: 'Rand', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'Wukong',
    awardLabel: 'Wukong grab',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const WUKONG_HELP: SlotHelpDefinition = {
  paylineCount: 23,
  freeGames: {
    requiredSymbols: 3,
    awardedSpins: 5,
  },
  extraSections: [
    {
      badge: 'PAW',
      title: 'Monkey paw money grab',
      body: 'A monkey paw anywhere on screen grabs every Rand multiplier coin showing in the window. Two paws are much rarer and double the grabbed amount. Three bananas in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'SEAL',
      title: 'Power seal collections',
      body: 'Sync, Rows, Paw, and Rand seals collect from anywhere visible. A completed 40-seal collection awards ten free spins tied to that collection’s average wager. Energy at 25%, 50%, and 75% improves seal odds; a full energy meter boosts the payout by 1.5×, resets, and finishes the nearest seal track.',
    },
  ],
}

export const WUKONG_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'fortune-forge-wukong-v1',
  cabinet: WUKONG_CABINET_THEME,
  features: WUKONG_FEATURES,
  help: WUKONG_HELP,
  shellBackdrop: 'default-clouds',
  symbols: WUKONG_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('classic-demo-v1'),
}

export const WUKONG_SLOT_GAME = defineSlotGame({
  id: 'wukong-journey-to-the-west',
  routes: {
    play: '/slots/wukong',
    demo: '/slots/wukong/demo',
  },
  catalog: WUKONG_CATALOG,
  experience: WUKONG_EXPERIENCE_SET,
})

import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { GODS_OF_OLYMPUS_CABINET_THEME } from './cabinetTheme'
import { GODS_OF_OLYMPUS_CATALOG } from './catalog'
import { GODS_OF_OLYMPUS_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Divine favor', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Olympian medallion collections',
    itemLabel: 'medallions',
    presentation: 'divine-offering',
    entries: [
      { id: 'sync', label: "Athena's strategy", shortLabel: 'Athena', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: "Poseidon's tide", shortLabel: 'Poseidon', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: "Ares' fury", shortLabel: 'Ares', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: "Hermes' fortune", shortLabel: 'Hermes', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The Gauntlet of Zeus',
    awardLabel: 'Olympian tribute',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 20,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'ZEUS',
      title: 'Olympian tribute',
      body: 'The Gauntlet of Zeus claims every drachma showing in the window. Two gauntlets double the tribute. Three lightning volleys in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'GODS',
      title: 'Four divine trials',
      body: 'Collect 40 medallions for Athena, Poseidon, Ares, or Hermes to unlock ten special free spins. Divine favor improves medallion odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest trial.',
    },
  ],
}

export const GODS_OF_OLYMPUS_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'gods-of-olympus-experience-v1',
  cabinet: GODS_OF_OLYMPUS_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  shellBackdrop: 'theme',
  symbols: GODS_OF_OLYMPUS_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('gods-of-olympus-v1'),
}

export const GODS_OF_OLYMPUS_SLOT_GAME = defineSlotGame({
  id: 'gods-of-olympus',
  routes: {
    play: '/slots/gods-of-olympus',
    demo: '/slots/gods-of-olympus/demo',
  },
  catalog: GODS_OF_OLYMPUS_CATALOG,
  experience: GODS_OF_OLYMPUS_EXPERIENCE_SET,
})

import './styles.css'
import { withEngineGuide } from '@fortuneforge/slot-game-runtime/engine-guide'
import { GODS_OF_OLYMPUS_SOUNDS } from './sounds'
import { createSlotRulesSet, type SlotExperienceSet } from '@fortuneforge/slot-game-runtime/experience'
import type { SlotFeatureSet, SlotHelpDefinition } from '@fortuneforge/slot-game-runtime'
import { defineSlotGame } from '@fortuneforge/slot-game-runtime/manifest'
import { GODS_OF_OLYMPUS_CABINET_THEME } from './cabinetTheme'
import { GODS_OF_OLYMPUS_CATALOG } from './catalog'
import { GODS_OF_OLYMPUS_SYMBOLS } from './symbols'
import { GodsTumbleAwardBadge } from './GodsTumbleAwardBadge'
import {
  GODS_TUMBLE_COLUMNS,
  GODS_TUMBLE_INITIAL_REELS,
  GODS_TUMBLE_ROWS,
  GODS_TUMBLE_SYMBOLS,
} from './tumble'

const FEATURES: SlotFeatureSet = {
  collections: {
    ariaLabel: 'Olympian medallion collections',
    itemLabel: 'medallions',
    presentation: 'divine-offering',
    entries: [
      { id: 'sync', label: "Athena's strategy", shortLabel: 'Athena', symbol: 'SEAL_SYNC', requiredCount: 28 },
      { id: 'rows', label: "Poseidon's tide", shortLabel: 'Poseidon', symbol: 'SEAL_ROWS', requiredCount: 28 },
      { id: 'paw', label: "Ares' fury", shortLabel: 'Ares', symbol: 'SEAL_PAW', requiredCount: 28 },
      { id: 'rand', label: "Hermes' fortune", shortLabel: 'Hermes', symbol: 'SEAL_RAND', requiredCount: 28 },
    ],
  },
  specialRound: {
    id: 'olympus-trial', title: 'Olympian Trial',
    earnLabel: 'Land 4 Olympus gates for 6 Trials, or complete 28 medallions for 6 enhanced trials.',
    earnStyle: 'altar', earnHint: 'Medallions are placed on four Olympian altars; complete one altar to begin a Trial.',
    activeModes: {
      sync: 'Athena’s Strategy · a matching reel is copied', rows: 'Poseidon’s Tide · two extra rows',
      paw: 'Ares’ Fury · extra wilds appear', rand: 'Hermes’ Fortune · a wild lane appears',
      'paw-rand': 'Olympian Favor · extra wilds with a wild lane',
    },
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 20,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 22, 23],
  freeGames: {
    requiredSymbols: 4,
    awardedSpins: 6,
    title: 'Olympian Gate Trial',
    symbolLabel: 'GATES OF OLYMPUS',
    awardLabel: 'Olympian Trial rounds',
  },
  extraSections: [
    {
      badge: 'ZEUS',
      title: 'Lightning volley',
      body: 'Three lightning volleys in a row, column, or diagonal pay 3× the wager. Olympian Trials use wild-enhanced reel effects instead of direct multiplier tokens.',
    },
    {
      badge: 'GODS',
      title: 'Four divine trials',
      body: 'Collect 28 medallions for Athena, Poseidon, Ares, or Hermes to unlock six enhanced Olympian Trials. Each completed altar immediately opens its matching Trial.',
    },
  ],
}

export const GODS_OF_OLYMPUS_EXPERIENCE_SET: SlotExperienceSet = withEngineGuide({
  id: 'gods-of-olympus-experience-v1',
  cabinet: GODS_OF_OLYMPUS_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  outcomeNarrative: {
    lossTitle: 'No tribute this spin',
    winTitle: 'Olympian tribute',
    greatWinTitle: 'Divine tribute',
    bigWinTitle: 'Godly jackpot',
    freeGameSingular: 'free divine spin',
    freeGamePlural: 'free divine spins',
    lossNextAction: 'Choose a wager, then seek the gods again.',
    winNextAction: 'Choose a wager, then claim another tribute.',
    bonusNextAction: 'Your next divine spin is free and uses the locked wager.',
  },
  shellBackdrop: 'theme',
  symbols: GODS_OF_OLYMPUS_SYMBOLS,
  mascot: null,
  sounds: GODS_OF_OLYMPUS_SOUNDS,
  rules: createSlotRulesSet('gods-of-olympus-v1', {
    tumble: {
      columns: GODS_TUMBLE_COLUMNS,
      rows: GODS_TUMBLE_ROWS,
      symbols: GODS_TUMBLE_SYMBOLS,
      initialReels: GODS_TUMBLE_INITIAL_REELS,
    },
  }),
  presentation: { TumbleAwardBadge: GodsTumbleAwardBadge },
})

export const GODS_OF_OLYMPUS_SLOT_GAME = defineSlotGame({
  id: 'gods-of-olympus',
  routes: {
    play: '/slots/gods-of-olympus',
    demo: '/slots/gods-of-olympus/demo',
  },
  catalog: GODS_OF_OLYMPUS_CATALOG,
  experience: GODS_OF_OLYMPUS_EXPERIENCE_SET,
})

import { WUKONG_TREASURES_SOUNDS } from '../../../features/slots/config/soundSets'
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
      {
        id: 'sync', label: 'Synced reels', displayLabel: 'Mirror Reel', shortLabel: 'Sync', symbol: 'SEAL_SYNC', requiredCount: 40,
        rewardDescription: 'Complete this orbit to launch 10 Celestial Quests where one reel mirrors another.',
      },
      {
        id: 'rows', label: 'Extra rows', displayLabel: '+2 Rows', shortLabel: '+2 rows', symbol: 'SEAL_ROWS', requiredCount: 40,
        rewardDescription: 'Complete this orbit to launch 10 Celestial Quests with two extra rows on every reel.',
      },
      {
        id: 'paw', label: 'Monkey paw rush', displayLabel: 'Paw Rush', shortLabel: 'Paws', symbol: 'SEAL_PAW', requiredCount: 40,
        rewardDescription: 'Complete this orbit to launch 10 Celestial Quests that add 2–5 monkey paws to every spin.',
      },
      {
        id: 'rand', label: 'Rand column', displayLabel: 'Rand Reel', shortLabel: 'Rand', symbol: 'SEAL_RAND', requiredCount: 40,
        rewardDescription: 'Complete this orbit to launch 10 Celestial Quests with a full prize-multiplier column.',
      },
    ],
  },
  moneyGrab: {
    actorName: 'Wukong',
    awardLabel: 'Wukong grab',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
  specialRound: {
    id: 'wukong-celestial-quest',
    title: 'Celestial Quest',
    earnLabel: 'Land 3 FREE GAME symbols for 5 quests, or complete a 40-seal orbit for 10 enhanced quests.',
    earnStyle: 'orbit',
    earnHint: 'Each seal circles its matching power. Complete one 40-seal orbit to unlock that power for ten Celestial Quests.',
    activeModes: {
      sync: 'Mirror Nimbus · one reel mirrors another',
      rows: 'Skyward Path · two extra rows open',
      paw: 'Monkey Paw Rush · 2–5 extra paws appear',
      rand: 'Fortune Cloud · a prize column appears',
    },
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
      body: 'Monkey paws are part of the normal symbol mix. One paw anywhere on screen grabs every Rand multiplier coin showing in the window; two or more paws double the grabbed amount. The Monkey Paw Rush collection adds 2–5 paws to every Celestial Quest spin. Three bananas in a row, column, or diagonal pay 3× the wager.',
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
  shellBackdrop: 'theme',
  symbols: WUKONG_SYMBOLS,
  mascot: null,
  sounds: WUKONG_TREASURES_SOUNDS,
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

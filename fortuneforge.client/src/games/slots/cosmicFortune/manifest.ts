import { COSMIC_FORTUNE_SOUNDS } from '../../../features/slots/config/soundSets'
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
      { id: 'sync', label: 'Binary link', shortLabel: 'Binary', symbol: 'SEAL_SYNC', requiredCount: 24 },
      { id: 'rows', label: 'Orbital expansion', shortLabel: 'Orbit', symbol: 'SEAL_ROWS', requiredCount: 24 },
      { id: 'paw', label: 'Tractor beam', shortLabel: 'Beam', symbol: 'SEAL_PAW', requiredCount: 24 },
      { id: 'rand', label: 'Star map', shortLabel: 'Map', symbol: 'SEAL_RAND', requiredCount: 24 },
    ],
  },
  specialRound: {
    id: 'cosmic-orbit', title: 'Orbit Run',
    earnLabel: 'Land 3 wormholes for 6 Orbit Runs, or complete 24 worlds for 7 enhanced runs.',
    earnStyle: 'orbit', earnHint: 'World tokens orbit their matching planet. Complete one orbit to launch an enhanced Orbit Run.',
    activeModes: {
      sync: 'Binary Link · one reel mirrors another', rows: 'Orbital Expansion · two extra rows',
      paw: 'Tractor Beam · extra wilds appear', rand: 'Star Map · a wild lane appears',
      'sync-rows': 'Orbit Alignment · linked reels with two extra rows',
    },
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 15,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 17, 18, 19, 20, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 6 },
  extraSections: [
    {
      badge: 'METEOR',
      title: 'Meteor shower',
      body: 'Three meteor showers in a row, column, or diagonal pay 3× the wager. Orbit Runs replace direct multiplier tokens with wild-enhanced reel effects.',
    },
    {
      badge: 'ORBIT',
      title: 'Planetary collections',
      body: 'Collect 24 worlds in any orbit to unlock seven enhanced Orbit Runs. Plasma charge improves planet odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest orbit.',
    },
  ],
}

export const COSMIC_FORTUNE_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'cosmic-fortune-experience-v1',
  cabinet: COSMIC_FORTUNE_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  outcomeNarrative: {
    lossTitle: 'No signal this spin',
    winTitle: 'Star reward',
    greatWinTitle: 'Galactic reward',
    bigWinTitle: 'Cosmic jackpot',
    freeGameSingular: 'free orbit',
    freeGamePlural: 'free orbits',
    lossNextAction: 'Choose a wager, then launch again when ready.',
    winNextAction: 'Choose a wager, then chart another orbit.',
    bonusNextAction: 'Your next orbit is free and uses the locked wager.',
  },
  shellBackdrop: 'theme',
  symbols: COSMIC_FORTUNE_SYMBOLS,
  mascot: null,
  sounds: COSMIC_FORTUNE_SOUNDS,
  rules: createSlotRulesSet('cosmic-fortune-v1'),
}

export const COSMIC_FORTUNE_SLOT_GAME = defineSlotGame({
  id: 'cosmic-fortune',
  routes: { play: '/slots/cosmic-fortune', demo: '/slots/cosmic-fortune/demo' },
  catalog: COSMIC_FORTUNE_CATALOG,
  experience: COSMIC_FORTUNE_EXPERIENCE_SET,
})

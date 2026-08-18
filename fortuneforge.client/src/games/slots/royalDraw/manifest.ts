import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { ROYAL_DRAW_CABINET_THEME } from './cabinetTheme'
import { ROYAL_DRAW_CATALOG } from './catalog'
import { ROYAL_DRAW_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Table heat', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Suit medallion collections',
    itemLabel: 'medallions',
    presentation: 'chip-stack',
    entries: [
      { id: 'sync', label: 'Hearts together', shortLabel: 'Hearts', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Diamond spread', shortLabel: 'Diamonds', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Club sweep', shortLabel: 'Clubs', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Spade stack', shortLabel: 'Spades', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The dealer chip tray',
    awardLabel: 'Pot sweep',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 17,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 19, 20, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'POT',
      title: 'Dealer pot sweep',
      body: 'The dealer chip tray sweeps every jackpot chip showing in the window. Two trays double the pot. Three card stacks in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'SUITS',
      title: 'Four-suit collections',
      body: 'Collect 40 heart, diamond, club, or spade medallions to unlock ten special free spins. Table heat improves medallion odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest suit track.',
    },
  ],
}

export const ROYAL_DRAW_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'royal-draw-experience-v1',
  cabinet: ROYAL_DRAW_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  shellBackdrop: 'theme',
  symbols: ROYAL_DRAW_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('royal-draw-v1'),
}

export const ROYAL_DRAW_SLOT_GAME = defineSlotGame({
  id: 'royal-draw',
  routes: { play: '/slots/royal-draw', demo: '/slots/royal-draw/demo' },
  catalog: ROYAL_DRAW_CATALOG,
  experience: ROYAL_DRAW_EXPERIENCE_SET,
})

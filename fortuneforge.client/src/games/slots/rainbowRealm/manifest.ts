import { RAINBOW_REALM_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { RAINBOW_REALM_CABINET_THEME } from './cabinetTheme'
import { RAINBOW_REALM_CATALOG } from './catalog'
import { RAINBOW_REALM_SYMBOLS } from './symbols'
import './rainbowRealmFeature.css'

const RAINBOW_REALM_FEATURES: SlotFeatureSet = {
  energy: {
    label: 'Sunshine',
    symbol: 'BOLT',
  },
  collections: {
    ariaLabel: 'Orchard charm collections',
    itemLabel: 'charms',
    presentation: 'juice-glass',
    entries: [
      { id: 'sync', label: 'Strawberry sync', displayLabel: 'Strawberry', shortLabel: 'Strawberry', symbol: 'SEAL_SYNC', requiredCount: 60 },
      { id: 'rows', label: 'Blueberry bounty', displayLabel: 'Blueberry', shortLabel: 'Blueberry', symbol: 'SEAL_ROWS', requiredCount: 60 },
      { id: 'paw', label: 'Basket harvest', displayLabel: 'Orange', shortLabel: 'Orange', symbol: 'SEAL_PAW', requiredCount: 60 },
      { id: 'rand', label: 'Kiwi token column', displayLabel: 'Kiwi', shortLabel: 'Kiwi', symbol: 'SEAL_RAND', requiredCount: 60 },
    ],
  },
  moneyGrab: {
    actorName: 'The wicker basket',
    awardLabel: 'Basket harvest',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
  specialRound: {
    id: 'rainbow-orchard-rush',
    title: 'Orchard Rush',
    earnLabel: 'Land 3 orchard gates or fill a 60-charm glass to start a 10-spin Orchard Rush bonus round.',
    earnStyle: 'glass',
    earnHint: 'Fruit charms fill four orchard glasses. Finish one 60-charm glass to carry its power into ten Orchard Rush bonus spins.',
    activeModes: {
      sync: 'Strawberry Sync · one reel mirrors another',
      rows: 'Blueberry Bounty · two extra rows open',
      paw: 'Basket Harvest · stronger basket grabs appear',
      rand: 'Kiwi Column · a prize column appears',
      'sync-rows-paw-rand': 'Full Spectrum · linked reels, extra rows, stronger harvests, and a prize column',
    },
  },
}

const RAINBOW_REALM_HELP: SlotHelpDefinition = {
  paylineCount: 23,
  freeGames: {
    requiredSymbols: 3,
    awardedSpins: 10,
    badge: 'BONUS',
    title: 'Orchard Rush bonus',
    symbolLabel: 'ORCHARD GATE',
    awardLabel: 'Orchard Rush bonus spins',
  },
  extraSections: [
    {
      badge: 'BASKET',
      title: 'Wicker basket harvest',
      body: 'A wicker fruit basket anywhere on screen gathers every rainbow fruit token showing in the window. Two baskets are much rarer and double the harvest. Three rainbow banana bunches in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'CHARMS',
      title: 'Orchard charm collections',
      body: 'Strawberry, blueberry, orange, and kiwi charms collect from anywhere visible. A completed 60-charm track starts a ten-spin Orchard Rush bonus round. Sunshine at 25%, 50%, and 75% improves charm odds; a full meter boosts the next paying spin by 1.5×, resets, and finishes the nearest charm track.',
    },
  ],
}

export const RAINBOW_REALM_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'rainbow-realm-fruits-v1',
  cabinet: RAINBOW_REALM_CABINET_THEME,
  features: RAINBOW_REALM_FEATURES,
  help: RAINBOW_REALM_HELP,
  outcomeNarrative: {
    lossTitle: 'No orchard win this spin',
    winTitle: 'Fruit harvest',
    greatWinTitle: 'Bountiful harvest',
    bigWinTitle: 'Orchard jackpot',
    freeGameSingular: 'Orchard Rush bonus spin',
    freeGamePlural: 'Orchard Rush bonus spins',
    lossNextAction: 'Choose a wager, then tend the orchard again.',
    winNextAction: 'Choose a wager, then harvest again when ready.',
    bonusNextAction: 'Your Orchard Rush bonus round uses the locked wager.',
  },
  shellBackdrop: 'theme',
  symbols: RAINBOW_REALM_SYMBOLS,
  mascot: null,
  sounds: RAINBOW_REALM_SOUNDS,
  rules: createSlotRulesSet('rainbow-realm-fruits-v1'),
}

export const RAINBOW_REALM_SLOT_GAME = defineSlotGame({
  id: 'rainbow-realm',
  routes: {
    play: '/slots/rainbow-realm',
    demo: '/slots/rainbow-realm/demo',
  },
  catalog: RAINBOW_REALM_CATALOG,
  experience: RAINBOW_REALM_EXPERIENCE_SET,
})

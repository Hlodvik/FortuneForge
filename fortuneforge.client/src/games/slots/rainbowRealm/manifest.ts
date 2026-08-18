import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { RAINBOW_REALM_CABINET_THEME } from './cabinetTheme'
import { RAINBOW_REALM_CATALOG } from './catalog'
import { RAINBOW_REALM_MASCOT } from './mascot'
import { RAINBOW_REALM_SYMBOLS } from './symbols'

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
      { id: 'sync', label: 'Strawberry sync', shortLabel: 'Strawberry', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Blueberry bounty', shortLabel: 'Blueberry', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Basket harvest', shortLabel: 'Orange', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Kiwi token column', shortLabel: 'Kiwi', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The wicker basket',
    awardLabel: 'Basket harvest',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const RAINBOW_REALM_HELP: SlotHelpDefinition = {
  paylineCount: 22,
  freeGames: {
    requiredSymbols: 3,
    awardedSpins: 5,
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
      body: 'Strawberry, blueberry, orange, and kiwi charms collect from anywhere visible. A completed 40-charm track awards ten special free spins. Sunshine at 25%, 50%, and 75% improves charm odds; a full meter boosts the payout by 1.5×, resets, and finishes the nearest charm track.',
    },
  ],
}

export const RAINBOW_REALM_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'rainbow-realm-fruits-v1',
  cabinet: RAINBOW_REALM_CABINET_THEME,
  features: RAINBOW_REALM_FEATURES,
  help: RAINBOW_REALM_HELP,
  shellBackdrop: 'theme',
  symbols: RAINBOW_REALM_SYMBOLS,
  mascot: RAINBOW_REALM_MASCOT,
  sounds: DEFAULT_SLOT_SOUNDS,
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

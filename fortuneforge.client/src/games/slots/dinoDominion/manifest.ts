import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { DINO_DOMINION_CABINET_THEME } from './cabinetTheme'
import { DINO_DOMINION_CATALOG } from './catalog'
import { DINO_DOMINION_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Meteor charge', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Fossil dig collections',
    itemLabel: 'fossils',
    presentation: 'fossil-dig',
    entries: [
      { id: 'sync', label: 'Predator pack', shortLabel: 'Fang', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Deep strata', shortLabel: 'Shell', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Fossil rush', shortLabel: 'Track', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Amber vein', shortLabel: 'Leaf', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The paleontologist field kit',
    awardLabel: 'Museum haul',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 14,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 16, 17, 18, 19, 20, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'DIG',
      title: 'Museum haul',
      body: 'The paleontologist field kit gathers every amber token in the dig. Two kits double the haul. Three fossil claws in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'FOSSIL',
      title: 'Prehistoric dig collections',
      body: 'Excavate 40 fossils from any dig site to unlock ten themed free spins. Meteor charge improves fossil odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest dig.',
    },
  ],
}

export const DINO_DOMINION_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'dino-dominion-experience-v1',
  cabinet: DINO_DOMINION_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  shellBackdrop: 'theme',
  symbols: DINO_DOMINION_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('dino-dominion-v1'),
}

export const DINO_DOMINION_SLOT_GAME = defineSlotGame({
  id: 'dino-dominion',
  routes: { play: '/slots/dino-dominion', demo: '/slots/dino-dominion/demo' },
  catalog: DINO_DOMINION_CATALOG,
  experience: DINO_DOMINION_EXPERIENCE_SET,
})

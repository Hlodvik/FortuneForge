import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { ARCANE_ARCHIVES_CABINET_THEME } from './cabinetTheme'
import { ARCANE_ARCHIVES_CATALOG } from './catalog'
import { ARCANE_ARCHIVES_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Aether charge', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Forbidden rune shelves',
    itemLabel: 'runes',
    presentation: 'spellbook-shelf',
    entries: [
      { id: 'sync', label: 'Echo chamber', shortLabel: 'Echo', symbol: 'SEAL_SYNC', requiredCount: 40 },
      { id: 'rows', label: 'Moonlit stacks', shortLabel: 'Moon', symbol: 'SEAL_ROWS', requiredCount: 40 },
      { id: 'paw', label: 'Oracle sight', shortLabel: 'Oracle', symbol: 'SEAL_PAW', requiredCount: 40 },
      { id: 'rand', label: 'Fortune script', shortLabel: 'Script', symbol: 'SEAL_RAND', requiredCount: 40 },
    ],
  },
  moneyGrab: {
    actorName: 'The enchanted book satchel',
    awardLabel: 'Mana harvest',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 16,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 18, 19, 20, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'SPELL',
      title: 'Mana harvest',
      body: 'The enchanted book satchel gathers every mana shard in the window. Two satchels double the harvest. Three magic wands in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'RUNES',
      title: 'Forbidden rune shelves',
      body: 'Collect 40 runes on any shelf to unlock ten themed free spins. Aether charge improves rune odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest shelf.',
    },
  ],
}

export const ARCANE_ARCHIVES_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'arcane-archives-experience-v1',
  cabinet: ARCANE_ARCHIVES_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  shellBackdrop: 'theme',
  symbols: ARCANE_ARCHIVES_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('arcane-archives-v1'),
}

export const ARCANE_ARCHIVES_SLOT_GAME = defineSlotGame({
  id: 'arcane-archives',
  routes: { play: '/slots/arcane-archives', demo: '/slots/arcane-archives/demo' },
  catalog: ARCANE_ARCHIVES_CATALOG,
  experience: ARCANE_ARCHIVES_EXPERIENCE_SET,
})

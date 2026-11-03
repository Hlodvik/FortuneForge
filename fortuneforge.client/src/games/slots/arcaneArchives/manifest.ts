import { withEngineGuide } from '../shared/engineGuide'
import { ARCANE_ARCHIVES_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { ARCANE_ARCHIVES_CABINET_THEME } from './cabinetTheme'
import { ARCANE_ARCHIVES_CATALOG } from './catalog'
import { ARCANE_ARCHIVES_SYMBOLS } from './symbols'

const FEATURES: SlotFeatureSet = {
  lightningCollection: { collectorSymbol: 'BOLT' },
  collections: {
    ariaLabel: 'Forbidden seal shelves',
    itemLabel: 'seals',
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
  specialRound: {
    id: 'arcane-forbidden-chapter',
    title: 'Forbidden Chapter',
    earnLabel: 'Land 3 archive doors for 5 chapters, or shelve 40 matching seals for 10 enhanced chapters.',
    earnStyle: 'shelf',
    earnHint: 'Seals remain visible on four archive shelves. Complete one 40-seal shelf to open ten Forbidden Chapters.',
    activeModes: {
      sync: 'Echo Chamber · one reel mirrors another',
      rows: 'Moonlit Stacks · two extra rows open',
      paw: 'Oracle Sight · stronger harvests appear',
      rand: 'Fortune Script · a prize column appears',
      'sync-paw-rand': 'Grand Grimoire · linked reels, stronger harvests, and a prize column',
    },
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 16,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 18, 19, 20, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'LIGHTNING',
      title: 'Living Lightning collection',
      body: 'Land a Living Lightning Rune to collect every visible constellation on that spin. Open Book pays 0.5×, Aether pays 1×, and Fortune pays 2× the wager. Each constellation pays once, even when more than one Lightning Rune lands.',
    },
    {
      badge: 'SPELL',
      title: 'Mana harvest',
      body: 'The enchanted book satchel gathers every mana shard in the window. Two satchels double the harvest. Three magic wands in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'SEALS',
      title: 'Forbidden seal shelves',
      body: 'Collect 40 seals on any shelf to unlock ten themed free spins.',
    },
  ],
}

export const ARCANE_ARCHIVES_EXPERIENCE_SET: SlotExperienceSet = withEngineGuide({
  id: 'arcane-archives-experience-v1',
  cabinet: ARCANE_ARCHIVES_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  outcomeNarrative: {
    lossTitle: 'No spell reward this spin',
    winTitle: 'Arcane reward',
    greatWinTitle: 'Arcane surge',
    bigWinTitle: 'Forbidden jackpot',
    freeGameSingular: 'free spell',
    freeGamePlural: 'free spells',
    lossNextAction: 'Choose a wager, then open the archives again.',
    winNextAction: 'Choose a wager, then cast another spell.',
    bonusNextAction: 'Your next spell is free and uses the locked wager.',
  },
  shellBackdrop: 'theme',
  symbols: ARCANE_ARCHIVES_SYMBOLS,
  mascot: null,
  sounds: ARCANE_ARCHIVES_SOUNDS,
  rules: createSlotRulesSet('arcane-archives-v1'),
})

export const ARCANE_ARCHIVES_SLOT_GAME = defineSlotGame({
  id: 'arcane-archives',
  routes: { play: '/slots/arcane-archives', demo: '/slots/arcane-archives/demo' },
  catalog: ARCANE_ARCHIVES_CATALOG,
  experience: ARCANE_ARCHIVES_EXPERIENCE_SET,
})

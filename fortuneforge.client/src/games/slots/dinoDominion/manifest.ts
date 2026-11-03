import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import { GOLD_FOUNDRY_GAME_ID, type SlotFeatureSet, type SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { DINO_DOMINION_CABINET_THEME } from './cabinetTheme'
import { DINO_DOMINION_CATALOG } from './catalog'
import { DINO_DOMINION_SYMBOLS, FOUNDRY_INGOT_IDS } from './symbols'
import { GOLD_CRUCIBLE_POUR_MS, GoldCrucible } from './GoldCrucible'
import { GOLD_FOUNDRY_SOUNDS } from './sounds'

const FEATURES: SlotFeatureSet = {
  energy: {
    label: 'Gold crucible',
    symbol: 'BOLT',
    presentation: 'crucible',
    artwork: GoldCrucible,
    completionDurationMs: GOLD_CRUCIBLE_POUR_MS,
  },
  moneyGrab: {
    actorName: 'The reel crucible',
    awardLabel: 'Ingot haul',
    collectorSymbol: 'PAW',
    valueSymbols: FOUNDRY_INGOT_IDS,
    presentation: 'separate-award',
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 14,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 16, 17, 18, 19, 20, 21, 22, 23],
  fourMatchPayoutNote: 'Four matching symbols pay their own rate. Wild substitution applies only to five-symbol matches.',
  extraSections: [
    {
      badge: 'CAST',
      title: 'Reel crucible',
      body: 'When a reel crucible lands, it pays for every visible ingot, even if the ingots are on different reels or already paid on a line. Each iron ingot pays 0.5× the wager, bronze 1×, copper 1.5×, and gold 2×. Two or more reel crucibles double the haul. The reel crucible itself never counts toward a payline.',
    },
    {
      badge: 'POUR',
      title: 'Filling crucible',
      body: 'Each payback ingot bundle adds one charge to the crucible above the reels. A paid spin that lands one or more bundles adds its wager once to the payback total. At 100 charge, the crucible pays 15% of that total, rounded down to the cent. Payback bundles do not pay on lines or count toward an ingot haul.',
    },
    {
      badge: 'HAMMER',
      title: 'Hammer award',
      body: 'Three smithing hammers in a row, column, or diagonal pay 3× the wager per qualifying pattern.',
    },
  ],
}

export const DINO_DOMINION_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'dino-dominion-experience-v1',
  cabinet: DINO_DOMINION_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  outcomeNarrative: {
    lossTitle: 'No win this spin',
    winTitle: 'Foundry win',
    greatWinTitle: 'Golden haul',
    bigWinTitle: 'Big foundry win',
    freeGameSingular: 'free spin',
    freeGamePlural: 'free spins',
    lossNextAction: 'Choose a wager, then spin again when ready.',
    winNextAction: 'Choose a wager, then spin again when ready.',
    bonusNextAction: 'Your next spin is free and uses the locked wager.',
  },
  shellBackdrop: 'theme',
  symbols: DINO_DOMINION_SYMBOLS,
  mascot: null,
  sounds: GOLD_FOUNDRY_SOUNDS,
  rules: createSlotRulesSet(GOLD_FOUNDRY_GAME_ID, {
    initialReels: [
      ['2', 'SEAL_ROWS', '6', 'BOLT'],
      ['5', '3', 'BANANA', 'SEAL_SYNC'],
      ['4', 'FREE', 'SEAL_PAW', '7'],
      ['POWER', 'SEAL_RAND', '2', 'ACE'],
      ['7', '6', '5', '3'],
    ],
  }),
}

export const DINO_DOMINION_SLOT_GAME = defineSlotGame({
  id: 'dino-dominion',
  routes: { play: '/slots/dino-dominion', demo: '/slots/dino-dominion/demo' },
  catalog: DINO_DOMINION_CATALOG,
  experience: DINO_DOMINION_EXPERIENCE_SET,
})

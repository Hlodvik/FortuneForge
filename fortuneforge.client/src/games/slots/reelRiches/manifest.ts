import { REEL_RICHES_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type { SlotFeatureSet, SlotHelpDefinition } from '../../../features/slots/config/slotFeatures'
import { defineSlotGame } from '../shared/slotGameManifest'
import { REEL_RICHES_CABINET_THEME } from './cabinetTheme'
import { REEL_RICHES_CATALOG } from './catalog'
import { REEL_RICHES_SYMBOLS } from './symbols'
import crimsonMound from '../../../assets/slots/games/reel-riches/optimized/reel-riches-progress-mound-crimson-v1.webp'
import sapphireMound from '../../../assets/slots/games/reel-riches/optimized/reel-riches-progress-mound-sapphire-v1.webp'
import amberMound from '../../../assets/slots/games/reel-riches/optimized/reel-riches-progress-mound-amber-v1.webp'
import emeraldMound from '../../../assets/slots/games/reel-riches/optimized/reel-riches-progress-mound-emerald-v1.webp'

const FEATURES: SlotFeatureSet = {
  energy: { label: 'Gem Multiplier', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Tackle badge collections',
    itemLabel: 'badges',
    presentation: 'tackle-creel',
    entries: [
      { id: 'sync', label: 'Perfect cast', shortLabel: 'Lure', symbol: 'SEAL_SYNC', requiredCount: 40, containerImage: crimsonMound },
      { id: 'rows', label: 'Rising tide', shortLabel: 'Wave', symbol: 'SEAL_ROWS', requiredCount: 40, containerImage: sapphireMound },
      { id: 'paw', label: 'Net frenzy', shortLabel: 'Net', symbol: 'SEAL_PAW', requiredCount: 40, containerImage: amberMound },
      { id: 'rand', label: 'Jackpot hook', shortLabel: 'Hook', symbol: 'SEAL_RAND', requiredCount: 40, containerImage: emeraldMound },
    ],
  },
  moneyGrab: {
    actorName: 'The fishing net',
    awardLabel: 'Net haul',
    collectorSymbol: 'PAW',
    valueSymbolPrefix: 'RAND_',
  },
  specialRound: {
    id: 'reel-riches-trophy-catch',
    title: 'Trophy Catch',
    earnLabel: 'Land 3 moonlit piers for 5 casts, or complete 40 tackle badges for 10 enhanced casts.',
    earnStyle: 'buckets',
    earnHint: 'Every tackle badge grows its own prize pile. Complete one 40-badge collection to launch ten Trophy Catch spins.',
    activeModes: {
      sync: 'Perfect Cast · one reel mirrors another',
      rows: 'Rising Tide · two extra rows open',
      paw: 'Net Frenzy · stronger net hauls appear',
      rand: 'Jackpot Hook · a prize column appears',
      'sync-rows-rand': 'Legendary Catch · linked reels, extra rows, and a prize column',
    },
  },
}

const HELP: SlotHelpDefinition = {
  paylineCount: 19,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21, 22, 23],
  freeGames: { requiredSymbols: 3, awardedSpins: 5 },
  extraSections: [
    {
      badge: 'NET',
      title: 'Net haul',
      body: 'A fishing net catches every pearl token showing in the window. Two nets double the haul. Three golden fish schools in a row, column, or diagonal pay 3× the wager.',
    },
    {
      badge: 'TACKLE',
      title: 'Legendary tackle collections',
      body: 'Collect 40 lure, wave, net, or hook badges to unlock ten special free spins. The Gem Multiplier meter improves badge odds at each quarter; when full, it boosts the next paying spin by 1.5× and completes the nearest tackle track.',
    },
  ],
}

export const REEL_RICHES_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'reel-riches-experience-v1',
  cabinet: REEL_RICHES_CABINET_THEME,
  features: FEATURES,
  help: HELP,
  outcomeNarrative: {
    lossTitle: 'No catch this spin',
    winTitle: 'Catch landed',
    greatWinTitle: 'Legendary catch',
    bigWinTitle: 'Monster catch',
    freeGameSingular: 'free cast',
    freeGamePlural: 'free casts',
    lossNextAction: 'Choose a wager, then cast again when ready.',
    winNextAction: 'Choose a wager, then chase another catch.',
    bonusNextAction: 'Your next cast is free and uses the locked wager.',
  },
  shellBackdrop: 'theme',
  symbols: REEL_RICHES_SYMBOLS,
  mascot: null,
  sounds: REEL_RICHES_SOUNDS,
  rules: createSlotRulesSet('reel-riches-v1'),
}

export const REEL_RICHES_SLOT_GAME = defineSlotGame({
  id: 'reel-riches',
  routes: { play: '/slots/reel-riches', demo: '/slots/reel-riches/demo' },
  catalog: REEL_RICHES_CATALOG,
  experience: REEL_RICHES_EXPERIENCE_SET,
})

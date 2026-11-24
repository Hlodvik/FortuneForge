import './styles.css'
import { createSlotRulesSet, type SlotExperienceSet } from '@fortuneforge/slot-game-runtime/experience'
import type { SlotFeatureSet, SlotHelpDefinition } from '@fortuneforge/slot-game-runtime'
import { defineSlotGame } from '@fortuneforge/slot-game-runtime/manifest'
import { PHARAOHS_ASCENT_SOUNDS } from './sounds'
import { PHARAOHS_ASCENT_CABINET_THEME } from './cabinetTheme'
import { PHARAOHS_ASCENT_CATALOG } from './catalog'
import { PHARAOHS_ASCENT_SYMBOLS } from './symbols'
import { PHARAOHS_ASCENT_VISUALS as art } from './visuals'
import { PHARAOH_GAME_ID } from './types'
import { PHARAOH_WAGERING, PHARAOH_WAGER_OPTIONS } from './wagering'
import { PharaohHelp } from './PharaohHelp'
import { GUARDIANS } from './pharaoh'

const seals = { sync: 'SEAL_SYNC', rows: 'SEAL_ROWS', paw: 'SEAL_PAW', rand: 'SEAL_RAND' } as const
const FEATURES: SlotFeatureSet = {
  energy: { label: 'Sunfire', symbol: 'BOLT' },
  collections: {
    ariaLabel: 'Four guardian seal collections', itemLabel: 'seals', presentation: 'divine-offering',
    completionTitle: 'A guardian awakens', completionDetail: 'Seven enhanced Tomb Spins await.',
    entries: GUARDIANS.map(guardian => ({
      id: guardian.id, label: guardian.name, shortLabel: guardian.name,
      symbol: seals[guardian.id], requiredCount: 24, rewardDescription: guardian.effect,
    })),
  },
  specialRound: {
    id: 'pharaoh-ascent', title: 'Tomb Spins', earnStyle: 'gates',
    earnLabel: 'Three gates award six Tomb Spins. Restore 24 matching seals for seven guardian-enhanced spins.',
    earnHint: 'Your four guardian collections persist. Complete a seal to awaken its power inside the pyramid.',
    activeModes: {
      sync: 'Horus · mirrored reels', rows: 'Thoth · two extra rows',
      paw: 'Anubis · two extra wilds', rand: 'Wadjet · a full wild reel',
      'sync-rows': 'Horus + Thoth · mirrored reels and two extra rows',
    },
  },
}
const HELP: SlotHelpDefinition = {
  paylineCount: 23,
  freeGames: { requiredSymbols: 3, awardedSpins: 6, title: 'Pyramid gates', symbolLabel: 'gate scatters', awardLabel: 'Tomb Spins' },
  extraSections: [
    { badge: 'SCARAB', title: 'Scarab Trail', body: 'Edge-connected groups of at least three red scarabs pay once per group. Three, four, or five-plus use 3×, 5×, or 8× the wager. Diagonal contact does not connect.' },
    { badge: 'GUARDIAN', title: 'Guardian chambers', body: 'Restore 24 matching seals for seven enhanced Tomb Spins. Horus mirrors reels, Thoth opens two extra rows, Anubis adds two wilds, and Wadjet creates a full wild reel. New funded average wagers round down to whole points.' },
    { badge: 'SUNFIRE', title: 'The Sunfire blessing', body: 'Sunfire improves guardian-seal appearance at each quarter mark. At a steady bet, a full meter pays 1.5× on the next paying spin and completes the nearest guardian collection. The bonus uses the lower of your current bet and the average bet that filled the meter; fractional point bonuses round down.' },
  ],
}
export const PHARAOHS_ASCENT_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'pharaohs-ascent-experience-v1',
  cabinet: PHARAOHS_ASCENT_CABINET_THEME, features: FEATURES, help: HELP,
  outcomeNarrative: {
    lossTitle: 'The tomb keeps its secrets', winTitle: 'Buried reward',
    greatWinTitle: 'A royal discovery', bigWinTitle: 'Pharaoh’s bounty',
    freeGameSingular: 'Tomb Spin', freeGamePlural: 'Tomb Spins',
    lossNextAction: 'Choose your wager, then explore the temple again.',
    winNextAction: 'Your reward is settled. Continue when ready.',
    bonusNextAction: 'Your next Tomb Spin uses its locked wager with no additional charge.',
  },
  shellBackdrop: 'theme', symbols: PHARAOHS_ASCENT_SYMBOLS, mascot: null, sounds: PHARAOHS_ASCENT_SOUNDS,
  rules: createSlotRulesSet(PHARAOH_GAME_ID, {
    pointValueInCents: PHARAOH_WAGERING.pointValueInCents,
    wagerOptions: PHARAOH_WAGER_OPTIONS,
    defaultWagerRand: PHARAOH_WAGERING.defaultWagerPoints * PHARAOH_WAGERING.pointValueInCents / 100,
  }),
  presentation: {
    outcomePresentation: 'ambient',
    symbolValueGuide: { mode: 'modal' },
    stageWordmark: { src: art.wordmark, width: 960, height: 200, className: 'pharaoh-wordmark' },
    winHelp: { Content: PharaohHelp, title: 'A guide to Pharaoh’s Ascent', eyebrow: 'Guardian chambers and Scarab Trails', theme: 'pharaohs-ascent', firstOpenStorageKey: 'pharaohs-ascent-introduction-v1' },
  },
}
export const PHARAOHS_ASCENT_SLOT_GAME = defineSlotGame({
  id: 'pharaohs-ascent',
  routes: { play: '/slots/pharaohs-ascent', demo: null },
  catalog: PHARAOHS_ASCENT_CATALOG, experience: PHARAOHS_ASCENT_EXPERIENCE_SET,
})

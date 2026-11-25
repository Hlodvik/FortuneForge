import './styles.css'
import { defineSlotGame } from '@fortuneforge/slot-game-runtime/manifest'
import { createSlotRulesSet } from '@fortuneforge/slot-game-runtime/experience'
import { guide } from './guide'
import { FIFTH_SUN_CABINET, FIFTH_SUN_GAME_ID, FIFTH_SUN_TITLE } from './definition'
import { FIFTH_SUN_CATALOG } from './catalog'
import { FIFTH_SUN_SYMBOL_SET } from './symbols'
import { SUNSTONE_ECLIPSE_SOUNDS } from './sounds'
import { SunwheelPanel } from './SunwheelPanel'
import { SunstoneIntroduction } from './SunstoneIntroduction'

export const FIFTH_SUN_SLOT_GAME = defineSlotGame({
  id: 'robot-revolution',
  routes: { play: '/slots/sunstone-eclipse', demo: null },
  catalog: FIFTH_SUN_CATALOG,
  experience: {
    id: 'fifth-sun-experience-v3', cabinet: FIFTH_SUN_CABINET,
    symbols: FIFTH_SUN_SYMBOL_SET, sounds: SUNSTONE_ECLIPSE_SOUNDS,
    shellBackdrop: 'theme', mascot: null,
    rules: createSlotRulesSet(FIFTH_SUN_GAME_ID, {
      pointValueInCents: guide.pointValueInCents, defaultWagerRand: 3,
      wagerOptions: Array.from({ length: 145 }, (_, index) => 12 + index * 2),
      initialReels: [
        ['GLYPH_FLAME', 'GLYPH_RAIN', 'FEATHER', 'SERPENT'],
        ['GLYPH_WIND', 'GLYPH_EARTH', 'EAGLE', 'JAGUAR'],
        ['GLYPH_REED', 'FEATHER', 'SUNSTONE', 'WILD'],
        ['SERPENT', 'EAGLE', 'GLYPH_WIND', 'GLYPH_RAIN'],
        ['JAGUAR', 'SUNSTONE', 'GLYPH_FLAME', 'GLYPH_EARTH'],
      ],
    }),
    features: {
      specialRound: {
        id: 'sunstone-eclipse', title: 'Totality', earnStyle: 'altar',
        earnLabel: 'One Eclipse on the centre reel awakens five paid Eclipse rounds.',
        earnHint: 'One board. Three alignments. Awaken all three guardians to reveal Totality.',
        activeModes: {
          'solar-eclipse': 'Solar Eclipse · clockwise Sunwheel · saved wager',
          'lunar-eclipse': 'Lunar Eclipse · anticlockwise Sunwheel · saved wager',
          'arcade-rows': 'Saved Eclipse spins · expanded reels · Sunwheel awards ×2',
          'rows-arcade': 'Saved Eclipse spins · expanded reels',
          rows: 'Saved bonus spins · expanded reels',
        },
      },
    },
    help: {
      paylineCount: guide.paylinePatternIds.length, paylinePatternIds: guide.paylinePatternIds,
      wildSubstitution: true,
      fiveMatchPayoutNote: `All ${guide.paylinePatternIds.length} full payline patterns use the same symbol multipliers. Wilds substitute in three- and five-symbol wins.`,
      fourMatchPayoutNote: 'Ordinary four-symbol runs at either edge pay the three-symbol rate. Four Wilds, or four-symbol runs using wild substitution, do not pay.',
      extraSections: [
        { badge: 'I', title: 'The Sunwheel', body: 'Sunstones rotate three guardian paths across each settled board. On each path, match its guardian, Sunstones or Wilds in any three, four or five positions for 0.5×, 1× or 2× your wager.' },
        { badge: 'II', title: 'Guardian alignment', body: 'Two aligned guardians multiply each guardian award by two. Align all three for three times their awards. Each guardian award rounds down to a whole wager point.' },
        { badge: 'III', title: 'Totality', body: 'One Solar or Lunar Eclipse on reel three activates five paid rounds at the triggering wager. Each round checks three wheel alignments on one settled board. Awakened guardians remain linked for that round; all three reveal Totality, doubled by a Sunstone at the heart.' },
      ],
    },
    outcomeNarrative: {
      lossTitle: 'The temple rests', winTitle: 'Temple treasure', greatWinTitle: 'The guardians awaken', bigWinTitle: 'The Sunstone Eclipse rises',
      freeGameSingular: 'Saved bonus spin', freeGamePlural: 'Saved bonus spins',
      lossNextAction: '', winNextAction: '', bonusNextAction: 'Saved bonuses retain their original wager and terms.',
    },
    presentation: {
      SpinFeaturePanel: SunwheelPanel, featurePaylineIds: [1001, 1002, 1003, 1010, 1011, 1012, 1013, 1021, 1022, 1023],
      paymentAlerts: false, outcomePresentation: 'ambient',
      symbolValueGuide: { mode: 'modal', className: 'symbol-value-guide--fifth-sun' },
      winHelp: {
        Content: SunstoneIntroduction, title: `Welcome to ${FIFTH_SUN_TITLE}`,
        eyebrow: 'The Sunwheel', theme: 'sunstone-eclipse',
        firstOpenStorageKey: 'fortuneforge:fifth-sun:introduction:v2',
      },
    },
  },
})
export const SUNSTONE_ECLIPSE_SLOT_GAME = FIFTH_SUN_SLOT_GAME
export const ROBOT_REVOLUTION_SLOT_GAME = FIFTH_SUN_SLOT_GAME

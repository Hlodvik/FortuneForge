import './styles.css'
import { createShowcaseSlotGame } from '@fortuneforge/slot-game-runtime/showcase'
import { defineSlotGame } from '@fortuneforge/slot-game-runtime/manifest'
import { ROBOT_REVOLUTION_DEFINITION } from './definition'
import { SunwheelPanel } from './SunwheelPanel'
import { SunstoneIntroduction } from './SunstoneIntroduction'

const base = createShowcaseSlotGame(ROBOT_REVOLUTION_DEFINITION)

export const SUNSTONE_ECLIPSE_SLOT_GAME = defineSlotGame({
  ...base,
  routes: { play: '/slots/sunstone-eclipse', demo: '/slots/sunstone-eclipse/demo' },
  experience: {
    ...base.experience,
    symbols: {
      ...base.experience.symbols,
      guideNote: 'Ordinary wins round to the nearest wager point (25 cents); half points round up. Wilds substitute on five-symbol paylines. Sunwheel matches use their own guardian paths, and each guardian award rounds down to a whole point. Sunwheel awards are added to ordinary wins.',
    },
    presentation: {
      ...base.experience.presentation,
      SpinFeaturePanel: SunwheelPanel,
      featurePaylineIds: [1001, 1002, 1003, 1010],
      winHelp: {
        Content: SunstoneIntroduction,
        title: 'Welcome to Sunstone Eclipse',
        eyebrow: 'The Sunwheel',
        theme: 'sunstone-eclipse',
        firstOpenStorageKey: 'fortuneforge:sunstone-eclipse:introduction:v1',
      },
    },
  },
})

export const ROBOT_REVOLUTION_SLOT_GAME = SUNSTONE_ECLIPSE_SLOT_GAME

// Edited: 2026-11-18T18:24:49-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:00:18-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:44:43-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T14:43:56-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:27:28-06:00 | Computer: Vinnabok
import './styles.css'
import { getSlotEngineGuideContract } from '@fortuneforge/slot-game-runtime/engine-guide'
import { ARCANE_ARCHIVES_SOUNDS } from './sounds'
import { createSlotRulesSet, type SlotExperienceSet } from '@fortuneforge/slot-game-runtime/experience'
import type { SlotFeatureSet, SlotHelpDefinition } from '@fortuneforge/slot-game-runtime'
import { defineSlotGame } from '@fortuneforge/slot-game-runtime/manifest'
import { ARCANE_ARCHIVES_CABINET_THEME } from './cabinetTheme'
import { ARCANE_ARCHIVES_CATALOG } from './catalog'
import { ARCANE_ARCHIVES_GAME_ID, ARCANE_ARCHIVES_SYMBOLS } from './symbols'
import { ARCANE_ARCHIVES_WORDMARK } from './presentation'
import { ArcaneIntroduction } from './ArcaneIntroduction'
import { ArcaneLightningCollection } from './ArcaneLightningCollection'
import { ArcanePotionWager } from './ArcanePotionWager'
import { ArcaneTomePanel } from './ArcaneTomePanel'

const FEATURES: SlotFeatureSet = {}

const { paylinePatternIds } = getSlotEngineGuideContract(ARCANE_ARCHIVES_GAME_ID)
const HELP: SlotHelpDefinition = {
  paylineCount: paylinePatternIds.length,
  paylinePatternIds,
  fiveMatchPayoutNote: 'Every full payline uses the same symbol values.',
  wildSubstitution: false,
  fourMatchPayoutNote: 'Only runs of exactly three or five matching symbols pay.',
}

export const ARCANE_ARCHIVES_EXPERIENCE_SET: SlotExperienceSet = {
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
  rules: createSlotRulesSet(ARCANE_ARCHIVES_GAME_ID, {
    defaultWagerRand: 3,
    initialReels: [
      ['2', '3', 'MANDRAKE_ROOT', '6'],
      ['3', '4', '5', 'POWER'],
      ['5', '2', '4', '6'],
      ['4', '6', '5', '3'],
      ['BOLT', '3', '6', '2'],
    ],
  }),
  presentation: {
    winHelp: {
      Content: ArcaneIntroduction,
      title: 'Welcome to Arcane Archives',
      eyebrow: 'How to win',
      theme: 'arcane-archives',
      firstOpenStorageKey: 'fortuneforge:arcane-archives:introduction:v1',
    },
    winPresentation: 'payline-popups',
    LightningCollectionOverlay: ArcaneLightningCollection,
    TomePanel: ArcaneTomePanel,
    PotionWagerReadout: ArcanePotionWager,
    tomeTriggerSymbol: 'ACE',
    stageWordmark: {
      src: ARCANE_ARCHIVES_WORDMARK,
      width: 768,
      height: 512,
      className: 'arcane-archives__wordmark',
    },
  },
}

export const ARCANE_ARCHIVES_SLOT_GAME = defineSlotGame({
  id: 'arcane-archives',
  routes: { play: '/slots/arcane-archives', demo: '/slots/arcane-archives/demo' },
  catalog: ARCANE_ARCHIVES_CATALOG,
  experience: ARCANE_ARCHIVES_EXPERIENCE_SET,
})

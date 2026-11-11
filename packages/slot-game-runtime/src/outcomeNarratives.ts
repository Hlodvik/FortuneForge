export type SlotOutcomeNarrative = Readonly<{
  lossTitle: string
  winTitle: string
  greatWinTitle: string
  bigWinTitle: string
  freeGameSingular: string
  freeGamePlural: string
  lossNextAction: string
  winNextAction: string
  bonusNextAction: string
}>

export const DEFAULT_SLOT_OUTCOME_NARRATIVE: SlotOutcomeNarrative = {
  lossTitle: 'No win this spin',
  winTitle: 'Win',
  greatWinTitle: 'Great win',
  bigWinTitle: 'Big win',
  freeGameSingular: 'free game',
  freeGamePlural: 'free games',
  lossNextAction: 'Choose a wager, then spin when ready.',
  winNextAction: 'Choose a wager, then spin again when ready.',
  bonusNextAction: 'The next spin uses the locked free wager.',
}

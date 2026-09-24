import { describe, expect, it } from 'vitest'
import {
  describeSpinOutcome,
  getSlotOutcomePresentation,
  getWinPresentationTier,
  getSlotWinTier,
  isBigWinAward,
  selectOutcomeSoundEvent,
  selectWinSoundEvent,
  type SlotOutcomePresentationInput,
} from './spinPresentation'
import type { PaylinePayout } from '../types/slots'

describe('spin presentation', () => {
  it('treats a four-symbol payout as a regular win cue', () => {
    const payline: PaylinePayout = {
      paylineId: 1,
      amountPoints: 2,
      matches: [{
        amountPoints: 2,
        multiplier: 1,
        match: {
          paylineId: 1,
          symbolId: '2',
          matchLength: 4,
          positions: [0, 1, 2, 3].map((reel) => ({ reel, row: 0 })),
          wildPositions: [],
        },
      }],
    }

    expect(selectWinSoundEvent(payline, 5)).toBe('single-three')
  })

  it('scales celebration without changing the settled award', () => {
    expect(getSlotWinTier(5, 0.5)).toBe('great')
    expect(getSlotWinTier(500, 10)).toBe('big')
    expect(getSlotWinTier(2, 0.5)).toBe('regular')
  })

  it('makes the next free spin clear while retaining its settled award', () => {
    expect(describeSpinOutcome({
      awardRand: 12.5,
      wagerRand: 0.5,
      freeSpinsAwarded: 3,
    })).toMatchObject({
      kind: 'bonus',
      title: '3 free games won',
      awardRand: 12.5,
      nextAction: 'The next spin uses the locked free wager.',
    })
  })

  it('uses a game narrative for the settled outcome without changing the award', () => {
    expect(describeSpinOutcome({
      awardRand: 12.5,
      wagerRand: 0.5,
      freeSpinsAwarded: 0,
      narrative: {
        lossTitle: 'No catch',
        winTitle: 'Catch landed',
        greatWinTitle: 'Legendary catch',
        bigWinTitle: 'Monster catch',
        freeGameSingular: 'free cast',
        freeGamePlural: 'free casts',
        lossNextAction: 'Cast again.',
        winNextAction: 'Cast again.',
        bonusNextAction: 'Your next cast is free.',
      },
    })).toMatchObject({
      kind: 'great-win',
      title: 'Legendary catch',
      awardRand: 12.5,
      nextAction: 'Cast again.',
    })
  })

  it('uses an escalated sound for high-importance outcomes', () => {
    expect(selectOutcomeSoundEvent(null, 5, 'great')).toBe('premium')
    expect(selectOutcomeSoundEvent(null, 5, 'big')).toBe('five')
  })

  it('keeps a small return brief and reserves the celebration for materially larger wins', () => {
    expect(getWinPresentationTier(0.5, 0.5)).toBe('win')
    expect(getWinPresentationTier(25, 2.5)).toBe('big')
    expect(getWinPresentationTier(500, 5)).toBe('jackpot')
  })

  it('gives a feature award precedence and explains the next action', () => {
    const outcome = getSlotOutcomePresentation({
      ...outcomeInput(),
      lastFreeSpinsAwarded: 5,
      freeSpinsRemaining: 5,
      lastWin: 25,
    })

    expect(outcome).toMatchObject({
      label: 'Feature unlocked',
      title: '5 free games won',
      significance: 'major',
      tone: 'milestone',
    })
    expect(outcome.nextAction).toContain('Press Spin')
  })

  it('uses the same threshold for a big-win summary as the win flyover', () => {
    expect(isBigWinAward(499, 5)).toBe(false)
    expect(isBigWinAward(500, 5)).toBe(true)
    expect(isBigWinAward(499, 10)).toBe(false)
    expect(isBigWinAward(500, 10)).toBe(true)

    const outcome = getSlotOutcomePresentation({
      ...outcomeInput(),
      lastWin: 500,
    })
    expect(outcome.label).toBe('Big win')
    expect(outcome.significance).toBe('major')
  })

  it('does not call a new screen a loss before the player has spun', () => {
    expect(getSlotOutcomePresentation(outcomeInput()).label).toBe('Wager selected')

    const outcome = getSlotOutcomePresentation({
      ...outcomeInput(),
      hasCompletedSpin: true,
    })
    expect(outcome).toMatchObject({
      label: 'No line win',
      title: 'The reels are ready for the next spin',
    })
  })
})

function outcomeInput(): SlotOutcomePresentationInput {
  return {
    activeWager: 5,
    bestPayline: null,
    bestSymbolLabel: null,
    canAffordSelectedWager: true,
    demoAvailabilityMessage: null,
    energyLabel: null,
    gameTitle: 'Wukong’s Journey',
    hasCompletedSpin: false,
    isSpinning: false,
    lastEnergyAwarded: 0,
    lastEnergyMultiplierApplied: false,
    lastFreeSpinsAwarded: 0,
    lastWin: 0,
    spinError: null,
    spinStage: 'requesting',
    freeSpinsRemaining: 0,
    useFreeGameForNextSpin: false,
  }
}

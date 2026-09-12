import { describe, expect, it } from 'vitest'
import {
  describeSpinOutcome,
  getSlotWinTier,
  selectOutcomeSoundEvent,
  selectWinSoundEvent,
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
})

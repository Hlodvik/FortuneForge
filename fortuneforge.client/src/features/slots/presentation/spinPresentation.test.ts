import { describe, expect, it } from 'vitest'
import { selectWinSoundEvent } from './spinPresentation'
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
})

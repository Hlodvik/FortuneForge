import { describe, expect, it } from 'vitest'
import { CANDY_CARNIVAL_SOUNDS } from '@fortuneforge/games-candy-carnival/sounds'
import { PIRATES_FORTUNE_SOUNDS } from '@fortuneforge/games-pirates-fortune/sounds'
import { DEFAULT_SLOT_SOUNDS } from '../config/soundSets'
import type { SlotResultSoundEvent } from '../config/soundSets'
import type { SpinResult } from '../types/slots'
import { getCollectionArrivalCue, getSuiteResultCue } from './slotSoundEvents'

function result(changes: Partial<SpinResult> = {}): SpinResult {
  return { isFreeSpin: false, freeSpinsRemaining: 0, bananaBonusPoints: 0,
    payout: { totalPoints: 0, paylines: [] }, ...changes } as SpinResult
}

describe('opt-in suite event selection', () => {
  it('gives actual bonus completion sole priority over every final-spin result', () => {
    const finalSpin = result({ isFreeSpin: true, bananaBonusPoints: 300 })
    const events: (SlotResultSoundEvent | null)[] = ['bonus', 'five', 'premium', 'single-three', 'no-win', null]
    for (const event of events) {
      expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, finalSpin, event, true, 0)).toBe('bonus-complete')
    }
  })

  it('does not infer completion from idle/base/retriggered/continuing states', () => {
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, result(), null, false, 0)).toBeUndefined()
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, result(), 'no-win', true, 0)).toBe('soft-miss')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, result({ isFreeSpin: true, freeSpinsRemaining: 1 }), 'no-win', true, 0)).toBe('soft-miss')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, result({ isFreeSpin: true }), 'bonus', true, 6)).toBe('bonus-entry')
  })

  it('uses the separate 777 award even when no ordinary payline pays, below bonus/major-win priority', () => {
    const tripleOnly = result({ bananaBonusPoints: 300 })
    expect(tripleOnly.payout.paylines).toEqual([])
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, tripleOnly, null, false, 0)).toBe('triple-sevens')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, tripleOnly, 'single-three', false, 0)).toBe('triple-sevens')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, tripleOnly, 'no-win', false, 0)).toBe('triple-sevens')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, tripleOnly, 'bonus', false, 6)).toBe('bonus-entry')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, tripleOnly, 'five', false, 0)).toBe('five-casino-win')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, tripleOnly, 'premium', false, 0)).toBe('premium-win')
    expect(getSuiteResultCue(CANDY_CARNIVAL_SOUNDS, result(), 'single-three', false, 0)).toBe('low-win')
  })

  it('leaves legacy result and collection dispatch unchanged', () => {
    for (const sounds of [DEFAULT_SLOT_SOUNDS, PIRATES_FORTUNE_SOUNDS]) {
      expect(getSuiteResultCue(sounds, result({ isFreeSpin: true, bananaBonusPoints: 300 }), 'five', true, 0)).toBeUndefined()
      expect(getCollectionArrivalCue(sounds, 'sync')).toBeUndefined()
    }
    expect(['sync', 'rows', 'paw', 'rand'].map(track => getCollectionArrivalCue(CANDY_CARNIVAL_SOUNDS, track)))
      .toEqual(['collect-sync', 'collect-rows', 'collect-paw', 'collect-rand'])
    expect(getCollectionArrivalCue(CANDY_CARNIVAL_SOUNDS, 'unknown')).toBeUndefined()
  })
})

import { describe, expect, it } from 'vitest'
import { getPresentedFreeSpinsAwarded } from './freeSpinAwards'

describe('queued Midnight bonus presentation', () => {
  it('does not announce a new award when the last spin advances to an already queued round', () => {
    expect(getPresentedFreeSpinsAwarded({ gameId: 'dragon-hoard-v1', freeSpinsAwarded: 0,
      freeSpinsRemaining: 10, awardedBonusRounds: [] }, 1, true)).toBe(0)
  })

  it('announces newly earned rounds even when they queue behind an unfinished round', () => {
    expect(getPresentedFreeSpinsAwarded({ gameId: 'dragon-hoard-v1', freeSpinsAwarded: 0,
      freeSpinsRemaining: 8, awardedBonusRounds: [{ id: 'new', totalSpins: 10, remainingSpins: 10,
        wagerPoints: 3, featureMode: 'rows-paw', mathModelId: 'midnight-vault-paytable-v2' }] }, 9, true)).toBe(10)
  })
})

import { describe, expect, it } from 'vitest'
import { bidLabel, nextBid } from './liarsDiceHelpers'

describe("Liar's Dice helpers", () => {
  it('formats and raises exact-face bids', () => {
    expect(bidLabel({ quantity: 3, face: 4 })).toBe('3 × 4s')
    expect(nextBid({ quantity: 3, face: 6 }, 12)).toEqual({ quantity: 4, face: 1 })
  })
})

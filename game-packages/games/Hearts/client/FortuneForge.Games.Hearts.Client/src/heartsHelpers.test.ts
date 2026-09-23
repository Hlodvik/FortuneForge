import { describe, expect, it } from 'vitest'
import { cardColor, directionLabel, seatLabel } from './heartsHelpers'

describe('Hearts preview helpers', () => {
  it('labels pass directions and seats', () => {
    expect(directionLabel('across')).toBe('Across')
    expect(seatLabel('south')).toBe('South')
  })
  it('marks red and black card suits', () => {
    expect(cardColor({ code: 'Q|hearts', rank: 'queen', suit: 'hearts', label: 'Q♥' })).toBe('red')
    expect(cardColor({ code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' })).toBe('black')
  })
})

import { describe, expect, it } from 'vitest'
import { cardColor, directionLabel, legalCardRationale, relativeSeat, seatsClockwise, seatLabel } from './heartsHelpers'

describe('Hearts preview helpers', () => {
  it('labels pass directions and seats', () => {
    expect(directionLabel('across')).toBe('Across')
    expect(seatLabel('south')).toBe('South')
  })
  it('marks red and black card suits', () => {
    expect(cardColor({ code: 'Q|hearts', rank: 'queen', suit: 'hearts', label: 'Q♥' })).toBe('red')
    expect(cardColor({ code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' })).toBe('black')
  })
  it('explains why following the led suit is required', () => {
    expect(legalCardRationale({
      phase: 'playing', yourTurn: true, heartsBroken: false,
      currentTrick: { leader: 'east', plays: [{ seat: 'east', card: { code: 'K|clubs', rank: 'king', suit: 'clubs', label: 'K♣' } }] },
      legalCards: [{ code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' }],
    })).toBe('Clubs were led, so you must follow clubs.')
  })
  it('pins every viewer at bottom while preserving clockwise seat order', () => {
    for (const human of seatsClockwise) {
      expect(relativeSeat(human, human)).toBe('bottom')
      const rotated = Array.from({ length: 4 }, (_, index) => seatsClockwise[(seatsClockwise.indexOf(human) + index) % 4])
      expect(rotated.map(seat => relativeSeat(seat, human))).toEqual(['bottom', 'left', 'top', 'right'])
    }
  })
})

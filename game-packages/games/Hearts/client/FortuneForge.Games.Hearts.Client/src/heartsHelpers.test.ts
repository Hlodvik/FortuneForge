import { describe, expect, it } from 'vitest'
import { botPersona, cardColor, directionLabel, legalCardRationale, seatLabel } from './heartsHelpers'

describe('Hearts preview helpers', () => {
  it('labels pass directions and seats', () => {
    expect(directionLabel('across')).toBe('Across')
    expect(seatLabel('south')).toBe('South')
  })
  it('marks red and black card suits', () => {
    expect(cardColor({ code: 'Q|hearts', rank: 'queen', suit: 'hearts', label: 'Q♥' })).toBe('red')
    expect(cardColor({ code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' })).toBe('black')
  })
  it('describes bot personalities', () => {
    expect(botPersona('east')).toContain('bold')
    expect(botPersona('west')).toContain('unpredictable')
  })
  it('explains why following the led suit is required', () => {
    expect(legalCardRationale({
      phase: 'playing', yourTurn: true, heartsBroken: false,
      currentTrick: { leader: 'east', plays: [{ seat: 'east', card: { code: 'K|clubs', rank: 'king', suit: 'clubs', label: 'K♣' } }] },
      legalCards: [{ code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' }],
    })).toBe('Clubs were led, so you must follow clubs.')
  })
})

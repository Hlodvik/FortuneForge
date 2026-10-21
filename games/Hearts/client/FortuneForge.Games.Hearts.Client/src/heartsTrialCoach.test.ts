import { describe, expect, it } from 'vitest'
import type { HeartsCard, HeartsMatch } from './contracts'
import { getTrialCoach } from './heartsTrialCoach'

const card = (rank: string, suit = 'clubs'): HeartsCard => ({ rank, suit, code: `${rank}|${suit}`, label: rank })
function match(values: Partial<HeartsMatch> = {}): HeartsMatch {
  return { matchId: 'trial', isTrial: true, targetScore: 100, roundNumber: 1, phase: 'passing', passDirection: 'left',
    turn: 'north', humanSeat: 'north', yourTurn: false, opponentsThinking: false, heartsBroken: false,
    submittedPasses: [], players: [], hand: [], legalCards: [], currentTrick: { leader: 'north', plays: [] },
    recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 },
    roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '', ...values }
}

describe('Hearts trial coach', () => {
  it('points to three own cards, favouring the queen and high spades', () => {
    const hand = [card('2'), card('queen', 'spades'), card('ace', 'spades'), card('king', 'hearts'), card('5', 'diamonds')]
    const coach = getTrialCoach(match({ hand }))
    expect(coach.kind).toBe('pass')
    expect(coach.suggestedCodes).toEqual(['queen|spades', 'ace|spades', 'king|hearts'])
    expect(new Set(coach.suggestedCodes).size).toBe(3)
    expect(coach.message).toContain('different cards')
  })
  it('points to confirm after any three own cards, not only its recommendations', () => {
    const hand = [card('2'), card('3'), card('4'), card('ace', 'hearts')]
    expect(getTrialCoach(match({ hand }), hand.slice(0, 3).map(value => value.code)).kind).toBe('confirm-pass')
    expect(getTrialCoach(match({ hand }), [hand[0].code, hand[0].code, 'other-player-card']).kind).toBe('pass')
  })
  it('stops pointing at cards after the human passes', () => {
    const coach = getTrialCoach(match({ submittedPasses: ['north'] }))
    expect(coach.kind).toBe('waiting'); expect(coach.suggestedCodes).toEqual([])
  })
  it('suggests the legal two of clubs on the opening lead', () => {
    const two = card('2'), queen = card('queen', 'spades')
    const coach = getTrialCoach(match({ phase: 'playing', yourTurn: true, hand: [two, queen], legalCards: [two] }))
    expect(coach.suggestedCodes).toEqual([two.code]); expect(coach.message).toContain('2 of clubs')
  })
  it('follows the led suit with a legal card below the current winner', () => {
    const low = card('3'), below = card('8'), high = card('king'), queen = card('queen', 'spades')
    const coach = getTrialCoach(match({ phase: 'playing', yourTurn: true, hand: [low, below, high, queen], legalCards: [low, below, high],
      currentTrick: { leader: 'east', plays: [{ seat: 'east', card: card('10') }] } }))
    expect(coach.suggestedCodes).toEqual([below.code]); expect(coach.title).toBe('Clubs it is—follow along!')
  })
  it('does not recommend an off-suit point card when following suit is required', () => {
    const king = card('king'), queen = card('queen', 'spades')
    const coach = getTrialCoach(match({ phase: 'playing', yourTurn: true, hand: [king, queen], legalCards: [king],
      currentTrick: { leader: 'west', plays: [{ seat: 'west', card: card('ace') }] } }))
    expect(coach.suggestedCodes).toEqual([king.code])
  })
  it('suggests shedding the queen of spades when a legal discard is allowed', () => {
    const queen = card('queen', 'spades'), heart = card('ace', 'hearts')
    const coach = getTrialCoach(match({ phase: 'playing', yourTurn: true, hand: [queen, heart], legalCards: [heart, queen],
      currentTrick: { leader: 'east', plays: [{ seat: 'east', card: card('king', 'diamonds') }] } }))
    expect(coach.suggestedCodes).toEqual([queen.code]); expect(coach.message).toContain('point card')
    expect(coach.why).toContain('13 points')
  })
  it('never suggests a card on someone else’s turn, after completion, or outside the viewer’s hand', () => {
    const own = card('2'), hidden = card('queen', 'spades')
    expect(getTrialCoach(match({ phase: 'playing', hand: [own], legalCards: [own] })).kind).toBe('waiting')
    expect(getTrialCoach(match({ phase: 'complete' })).suggestedCodes).toEqual([])
    expect(getTrialCoach(match({ phase: 'playing', yourTurn: true, hand: [own], legalCards: [hidden] })).suggestedCodes).toEqual([])
  })
  it('explains clockwise rotation, the opening lead, and who leads the next trick', () => {
    const coach = getTrialCoach(match({ phase: 'playing', turn: 'east', players: [{ seat: 'east', displayName: 'Trial bot 1', handCount: 13, matchScore: 0, roundScore: 0, hasPassed: true }] }))
    expect(coach.title).toBe('Trial bot 1’s turn')
    expect(coach.message).toContain('clockwise')
    expect(coach.why).toContain('2 of clubs')
    expect(coach.why).toContain('wins a trick leads the next')
  })
  it('keeps low hearts rather than passing them before high non-point cards', () => {
    const hand = [card('2', 'hearts'), card('3', 'hearts'), card('queen', 'diamonds'), card('king'), card('ace', 'clubs')]
    const coach = getTrialCoach(match({ hand }))
    expect(coach.suggestedCodes).not.toContain('2|hearts')
    expect(coach.suggestedCodes).not.toContain('3|hearts')
    expect(coach.why).toContain('saving low')
  })
  it('explains why the current winner can take another trick and why low cards are worth saving', () => {
    const low = card('3'), below = card('8')
    const coach = getTrialCoach(match({ phase: 'playing', yourTurn: true, hand: [low, below], legalCards: [low, below],
      currentTrick: { leader: 'east', plays: [{ seat: 'east', card: card('10') }] } }))
    expect(coach.why).toContain('saves your tiny cards')
    const mustWin = getTrialCoach(match({ phase: 'playing', yourTurn: true, hand: [below], legalCards: [below],
      currentTrick: { leader: 'east', plays: [{ seat: 'east', card: card('2') }] } }))
    expect(mustWin.why).toContain('then leads again')
    expect(mustWin.why).toContain('point-free trick')
  })
})

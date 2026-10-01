import { describe, expect, it } from 'vitest'
import { appendRoundHistory, bidLabel, historyEntry, nextBid, outcomeLabel, playerLabel, validBid } from './liarsDiceHelpers'
import type { LiarsDiceMatch, LiarsDiceOutcome } from './contracts'

const matchId = '00000000-0000-4000-8000-000000000001'
const otherMatchId = '00000000-0000-4000-8000-000000000002'
const table: LiarsDiceMatch = {
  matchId, phase: 'bidding', roundNumber: 1, currentPlayerId: 'you', currentBid: null, currentBidderId: null,
  totalDice: 8, hand: [1, 4],
  players: [
    { id: 'you', displayName: 'You', diceCount: 2, active: true, isHuman: true },
    { id: 'bot-1', displayName: 'Amber Badger', diceCount: 2, active: true, isHuman: false },
    { id: 'bot-2', displayName: 'Copper Finch', diceCount: 2, active: true, isHuman: false },
    { id: 'bot-3', displayName: 'Silver Otter', diceCount: 2, active: true, isHuman: false },
  ],
  outcome: null, winner: null, opponentsThinking: false, message: 'Your turn',
}

function resolved(outcome: LiarsDiceOutcome = { challengerId: 'you', bidderId: 'bot-1', loserId: 'you', quantity: 2, face: 4, matchingDice: 3, callType: 'liar' }): LiarsDiceMatch {
  return { ...table, phase: 'resolved', currentBid: { quantity: outcome.quantity, face: outcome.face }, currentBidderId: outcome.bidderId, outcome }
}

describe("Liar's Dice bid presentation", () => {
  it('formats and raises exact-face bids', () => {
    expect(bidLabel({ quantity: 3, face: 4 })).toBe('3 × 4s')
    expect(bidLabel(null)).toBe('No bid yet')
    expect(nextBid({ quantity: 3, face: 6 }, 12)).toEqual({ quantity: 4, face: 1 })
  })

  it.each([1, 2, 3, 4, 5, 6])('allows an opening bid on ordinary face %s', face => {
    expect(validBid({ quantity: 1, face }, null, 8)).toBe(true)
    expect(validBid({ quantity: 8, face }, null, 8)).toBe(true)
  })

  it('allows a higher face at the same quantity or any face at a higher quantity', () => {
    const current = { quantity: 3, face: 4 }
    expect(validBid({ quantity: 3, face: 5 }, current, 8)).toBe(true)
    expect(validBid({ quantity: 4, face: 1 }, current, 8)).toBe(true)
    expect(validBid({ quantity: 3, face: 4 }, current, 8)).toBe(false)
    expect(validBid({ quantity: 3, face: 3 }, current, 8)).toBe(false)
    expect(validBid({ quantity: 2, face: 6 }, current, 8)).toBe(false)
  })

  it.each([0, -1, 1.5, 8.1, 9, NaN, Infinity, -Infinity])('rejects the entered quantity %s without rounding or clamping', quantity => {
    expect(validBid({ quantity, face: 4 }, null, 8)).toBe(false)
  })

  it.each([0, -1, 1.5, 7, NaN, Infinity])('rejects invalid face %s', face => {
    expect(validBid({ quantity: 1, face }, null, 8)).toBe(false)
  })

  it.each([0, -1, 1.5, NaN, Infinity])('offers no bids for an invalid total %s', total => {
    expect(validBid({ quantity: 1, face: 1 }, null, total)).toBe(false)
    expect(nextBid(null, total)).toBeNull()
  })

  it('rejects malformed current bids rather than offering misleading choices', () => {
    for (const current of [{ quantity: 0, face: 1 }, { quantity: 9, face: 1 }, { quantity: 1, face: 7 }, { quantity: 1.5, face: 2 }]) {
      expect(validBid({ quantity: 8, face: 6 }, current, 8)).toBe(false)
      expect(nextBid(current, 8)).toBeNull()
    }
  })

  it('starts at one one and gives the minimum raise across a face rollover', () => {
    expect(nextBid(null, 1)).toEqual({ quantity: 1, face: 1 })
    expect(nextBid({ quantity: 2, face: 3 }, 8)).toEqual({ quantity: 2, face: 4 })
    expect(nextBid({ quantity: 2, face: 6 }, 8)).toEqual({ quantity: 3, face: 1 })
    expect(nextBid({ quantity: 8, face: 5 }, 8)).toEqual({ quantity: 8, face: 6 })
  })

  it('returns no raise at the top bid instead of creating an illegal ninth die or lower face', () => {
    expect(nextBid({ quantity: 8, face: 6 }, 8)).toBeNull()
    for (let face = 1; face <= 6; face++) expect(validBid({ quantity: 8, face }, { quantity: 8, face: 6 }, 8)).toBe(false)
    expect(nextBid({ quantity: 1, face: 6 }, 1)).toBeNull()
  })
})

describe("Liar's Dice authoritative outcome labels", () => {
  it('uses public player names for the current turn', () => {
    expect(outcomeLabel(table)).toBe('Your turn')
    expect(outcomeLabel({ ...table, currentPlayerId: 'bot-2' })).toBe('Copper Finch to act')
    expect(playerLabel(table, 'bot-3')).toBe('Silver Otter')
    expect(playerLabel(table, 'visitor')).toBe('visitor')
    expect(playerLabel({ ...table, players:table.players.map(p => p.isHuman ? { ...p, displayName:'Alex' } : p) }, 'you')).toBe('You')
  })

  it('describes the server’s liar result and losing actor', () => {
    expect(outcomeLabel(resolved())).toBe('Liar · 3 matching 4s · You lose a die')
    expect(outcomeLabel(resolved({ challengerId: 'you', bidderId: 'bot-1', loserId: 'bot-1', quantity: 2, face: 3, matchingDice: 0, callType: 'liar' })))
      .toBe('Liar · 0 matching 3s · Amber Badger loses a die')
  })

  it('preserves Spot On’s single-die loss for the server-named bidder or challenger', () => {
    expect(outcomeLabel(resolved({ challengerId: 'you', bidderId: 'bot-1', loserId: 'bot-1', quantity: 3, face: 4, matchingDice: 3, callType: 'spot-on' })))
      .toBe('Spot On · 3 matching 4s · Amber Badger loses a die')
    expect(outcomeLabel(resolved({ challengerId: 'you', bidderId: 'bot-1', loserId: 'you', quantity: 2, face: 4, matchingDice: 3, callType: 'spot-on' })))
      .toBe('Spot On · 3 matching 4s · You lose a die')
  })

  it('supports the older challenge contract without callType', () => {
    expect(outcomeLabel(resolved({ challengerId: 'you', bidderId: 'bot-1', loserId: 'you', quantity: 2, face: 4, matchingDice: 3 })))
      .toBe('Liar · 3 matching 4s · You lose a die')
  })

  it('uses the server winner field without awarding a victory from local hand counts', () => {
    expect(outcomeLabel({ ...resolved(), winner: 'bot-3' })).toBe('Silver Otter wins the match')
    expect(outcomeLabel({ ...resolved(), winner: 'you' })).toBe('You win the match')
    expect(outcomeLabel({ ...table, hand: [6, 6, 6, 6, 6] })).toBe('Your turn')
    expect(outcomeLabel({ ...table, phase: 'resolved' })).toBe('Round resolved')
  })
})

describe("Liar's Dice match-and-round history", () => {
  it('records only a resolved server outcome, including the final match round', () => {
    expect(historyEntry(table)).toBeNull()
    expect(historyEntry({ ...table, phase: 'resolved' })).toBeNull()
    expect(historyEntry({ ...resolved(), winner: 'bot-3' })).toEqual({ matchId, round: 1, text: 'Liar · 3 matching 4s · You lose a die' })
  })

  it('deduplicates repeated GET outcomes for the same match and round', () => {
    const match = resolved()
    const once = appendRoundHistory([], match)
    expect(appendRoundHistory(appendRoundHistory(once, match), match)).toEqual(once)
  })

  it('keeps round one of a new match distinct from round one of a previous match', () => {
    const first = resolved()
    const next = { ...resolved(), matchId: otherMatchId }
    expect(appendRoundHistory(appendRoundHistory([], first), next).map(entry => [entry.matchId, entry.round]))
      .toEqual([[otherMatchId, 1], [matchId, 1]])
  })

  it('keeps newest rounds first, respects the limit and leaves the supplied history intact', () => {
    const first = historyEntry(resolved())!
    const history = [first]
    const second = { ...resolved(), roundNumber: 2 }
    expect(appendRoundHistory(history, second).map(entry => entry.round)).toEqual([2, 1])
    expect(appendRoundHistory(history, second, 1).map(entry => entry.round)).toEqual([2])
    expect(history).toEqual([first])
  })

  it('cleans duplicate stored entries and does not record a bidding response as an outcome', () => {
    const first = historyEntry(resolved())!
    expect(appendRoundHistory([first, first], table)).toEqual([first])
    expect(appendRoundHistory([], { ...table, outcome: resolved().outcome })).toEqual([])
  })

  it('replaces an existing round entry with the latest authoritative response without duplication', () => {
    const old = { matchId, round: 1, text: 'Old display text' }
    expect(appendRoundHistory([old], resolved())).toEqual([historyEntry(resolved())])
  })

  it.each([0, -1, 1.5, NaN, Infinity])('returns no history for invalid limit %s', limit => {
    expect(appendRoundHistory([], resolved(), limit)).toEqual([])
  })
})

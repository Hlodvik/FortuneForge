import { describe, expect, it } from 'vitest'
import type { VideoPokerCard, VideoPokerCardPosition, VideoPokerHandRank } from './contracts'
import { handLabel, paytableRows, recommendedHolds } from './videoPokerPresentation'

const card = (rank: VideoPokerCard['rank'], suit: VideoPokerCard['suit']): VideoPokerCard => ({ rank, suit })

describe('recommendedHolds', () => {
  const madeHands: [string, readonly VideoPokerCard[]][] = [
    ['royal flush', [card('ace', 'spades'), card('queen', 'spades'), card('ten', 'spades'), card('king', 'spades'), card('jack', 'spades')]],
    ['flush', [card('two', 'clubs'), card('king', 'clubs'), card('eight', 'clubs'), card('five', 'clubs'), card('ten', 'clubs')]],
    ['straight', [card('seven', 'clubs'), card('three', 'hearts'), card('six', 'diamonds'), card('four', 'spades'), card('five', 'hearts')]],
    ['ace-high straight', [card('ace', 'clubs'), card('queen', 'hearts'), card('ten', 'spades'), card('king', 'spades'), card('jack', 'diamonds')]],
    ['ace-low straight', [card('four', 'hearts'), card('ace', 'clubs'), card('two', 'diamonds'), card('five', 'clubs'), card('three', 'spades')]],
    ['straight flush', [card('nine', 'diamonds'), card('seven', 'diamonds'), card('ten', 'diamonds'), card('eight', 'diamonds'), card('six', 'diamonds')]],
  ]

  it.each(madeHands)('preserves all five cards of an unsorted made %s', (_name, cards) => {
    expect(recommendedHolds(cards)).toEqual([0, 1, 2, 3, 4])
  })

  const groupedHands: [string, readonly VideoPokerCard[], VideoPokerCardPosition[]][] = [
    ['low pair ahead of unrelated high cards', [card('ace', 'clubs'), card('five', 'hearts'), card('jack', 'diamonds'), card('king', 'clubs'), card('five', 'spades')], [1, 4]],
    ['high pair', [card('queen', 'spades'), card('three', 'hearts'), card('king', 'clubs'), card('queen', 'clubs'), card('seven', 'diamonds')], [0, 3]],
    ['interleaved two pair', [card('five', 'clubs'), card('ace', 'hearts'), card('nine', 'spades'), card('nine', 'diamonds'), card('five', 'hearts')], [0, 2, 3, 4]],
    ['three of a kind', [card('seven', 'clubs'), card('ace', 'hearts'), card('seven', 'spades'), card('jack', 'diamonds'), card('seven', 'hearts')], [0, 2, 4]],
    ['full house', [card('king', 'clubs'), card('three', 'hearts'), card('king', 'spades'), card('three', 'diamonds'), card('king', 'hearts')], [0, 1, 2, 3, 4]],
    ['four of a kind', [card('two', 'clubs'), card('two', 'hearts'), card('king', 'clubs'), card('two', 'diamonds'), card('two', 'spades')], [0, 1, 3, 4]],
    ['duplicate-rank near-straight', [card('two', 'clubs'), card('three', 'hearts'), card('four', 'spades'), card('five', 'diamonds'), card('five', 'hearts')], [3, 4]],
  ]

  it.each(groupedHands)('preserves the original grouped positions for %s', (_name, cards, positions) => {
    expect(recommendedHolds(cards)).toEqual(positions)
  })

  it('uses only high-card positions when there is no made hand or group', () => {
    const cards = [card('two', 'clubs'), card('seven', 'hearts'), card('jack', 'diamonds'), card('ace', 'clubs'), card('queen', 'spades')]

    expect(recommendedHolds(cards)).toEqual([2, 3, 4])
  })

  it('does not invent holds for an unpaired low-card hand', () => {
    const cards = [card('two', 'clubs'), card('four', 'hearts'), card('seven', 'diamonds'), card('nine', 'clubs'), card('ten', 'spades')]

    expect(recommendedHolds(cards)).toEqual([])
  })

  it('does not reorder or modify the supplied server cards', () => {
    const cards = Object.freeze([
      Object.freeze(card('four', 'hearts')), Object.freeze(card('ace', 'clubs')), Object.freeze(card('two', 'diamonds')),
      Object.freeze(card('five', 'clubs')), Object.freeze(card('three', 'spades')),
    ])
    const originalCards = cards.map(value => ({ ...value }))

    expect(recommendedHolds(cards)).toEqual([0, 1, 2, 3, 4])
    expect(cards).toEqual(originalCards)
  })
})

describe('handLabel', () => {
  const labels: [VideoPokerHandRank, number, string][] = [
    ['no-win', 0, 'No Win'],
    ['pair', 0, 'Pair'],
    ['pair', 1, 'Jacks or Better'],
    ['two-pair', 2, 'Two Pair'],
    ['three-of-a-kind', 3, 'Three of a Kind'],
    ['straight', 4, 'Straight'],
    ['flush', 6, 'Flush'],
    ['full-house', 9, 'Full House'],
    ['four-of-a-kind', 25, 'Four of a Kind'],
    ['straight-flush', 50, 'Straight Flush'],
    ['royal-flush', 4_000, 'Royal Flush'],
  ]

  it.each(labels)('labels %s with payout %s as %s', (rank, payout, label) => {
    expect(handLabel(rank, payout)).toBe(label)
  })
})

describe('paytableRows', () => {
  const handOrder = [
    'Royal Flush', 'Straight Flush', 'Four of a Kind', 'Full House', 'Flush',
    'Straight', 'Three of a Kind', 'Two Pair', 'Jacks or Better',
  ]
  const selectedCoinSchedules: [number, number[]][] = [
    [1, [250, 50, 25, 9, 6, 4, 3, 2, 1]],
    [2, [500, 100, 50, 18, 12, 8, 6, 4, 2]],
    [3, [750, 150, 75, 27, 18, 12, 9, 6, 3]],
    [4, [1_000, 200, 100, 36, 24, 16, 12, 8, 4]],
    [5, [4_000, 250, 125, 45, 30, 20, 15, 10, 5]],
  ]

  it.each(selectedCoinSchedules)('shows the ordered 9/6 schedule for %s selected coins', (coins, payouts) => {
    const rows = paytableRows(coins)

    expect(rows.map(row => row.hand)).toEqual(handOrder)
    expect(rows.map(row => row.payout)).toEqual(payouts)
  })
})

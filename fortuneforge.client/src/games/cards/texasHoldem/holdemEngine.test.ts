import { describe, expect, it } from 'vitest'
import { createShuffledDeck, type CardRank, type CardSuit, type PlayingCardModel } from '../shared/cards'
import {
  compareHandEvaluations,
  createHoldemGame,
  evaluateBestHoldemHand,
  playHoldemAction,
} from './holdemEngine'

function card(rank: CardRank, suit: CardSuit): PlayingCardModel {
  return { id: `${suit}-${rank}`, rank, suit }
}

describe('Texas Hold’em engine', () => {
  it('reuses a deterministic 52-card deck with no duplicates', () => {
    const first = createShuffledDeck(8675309)
    const second = createShuffledDeck(8675309)

    expect(first).toHaveLength(52)
    expect(new Set(first.map((value) => value.id)).size).toBe(52)
    expect(second).toEqual(first)
  })

  it('selects a straight flush as the best five cards from seven', () => {
    const hand = evaluateBestHoldemHand([
      card(1, 'spades'),
      card(13, 'spades'),
      card(12, 'spades'),
      card(11, 'spades'),
      card(10, 'spades'),
      card(2, 'hearts'),
      card(2, 'clubs'),
    ])

    expect(hand.name).toBe('Straight Flush')
    expect(hand.tiebreak).toEqual([14])
  })

  it('recognizes an ace-low wheel straight', () => {
    const hand = evaluateBestHoldemHand([
      card(1, 'clubs'),
      card(2, 'diamonds'),
      card(3, 'spades'),
      card(4, 'hearts'),
      card(5, 'clubs'),
      card(13, 'diamonds'),
      card(12, 'spades'),
    ])

    expect(hand.name).toBe('Straight')
    expect(hand.tiebreak).toEqual([5])
  })

  it('uses kickers to break otherwise equal pairs', () => {
    const acesWithKing = evaluateBestHoldemHand([
      card(1, 'clubs'), card(1, 'diamonds'), card(13, 'spades'), card(9, 'hearts'), card(7, 'clubs'),
    ])
    const acesWithQueen = evaluateBestHoldemHand([
      card(1, 'hearts'), card(1, 'spades'), card(12, 'clubs'), card(9, 'diamonds'), card(7, 'spades'),
    ])

    expect(compareHandEvaluations(acesWithKing, acesWithQueen)).toBeGreaterThan(0)
  })

  it('deals blinds and advances through all four betting streets to showdown', () => {
    let game = createHoldemGame({ seed: 12345, dealer: 'player' })
    expect(game.playerHole).toHaveLength(2)
    expect(game.opponentHole).toHaveLength(2)
    expect(game.pot).toBe(30)
    expect(game.toCall).toBe(10)

    game = playHoldemAction(game, 'check-call')
    expect(game.stage).toBe('flop')
    expect(game.community).toHaveLength(3)
    game = playHoldemAction(game, 'check-call')
    expect(game.stage).toBe('turn')
    expect(game.community).toHaveLength(4)
    game = playHoldemAction(game, 'check-call')
    expect(game.stage).toBe('river')
    expect(game.community).toHaveLength(5)
    game = playHoldemAction(game, 'check-call')

    expect(game.status).toBe('complete')
    expect(game.result?.playerHand).not.toBeNull()
    expect(game.result?.opponentHand).not.toBeNull()
    expect(game.playerChips + game.opponentChips + game.pot).toBe(2_000)
  })

  it('awards the existing pot immediately when the player folds', () => {
    const game = playHoldemAction(createHoldemGame({ seed: 42, dealer: 'player' }), 'fold')

    expect(game.status).toBe('complete')
    expect(game.result?.winner).toBe('opponent')
    expect(game.playerChips).toBe(990)
    expect(game.opponentChips).toBe(1_010)
    expect(game.pot).toBe(0)
  })
})

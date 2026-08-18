import { describe, expect, it } from 'vitest'
import {
  applyLocalSolitaireCommand,
  autoFinishLocalSolitaire,
  canApplyLocalSolitaireCommand,
  createLocalSolitaireGame,
  firstLegalFoundation,
  SolitaireRuleError,
} from './solitaireEngine'
import type { SolitaireGame } from './solitaireTypes'

describe('local Klondike rules', () => {
  it('uses draw 3 by moving three cards from stock to waste', () => {
    const game = createLocalSolitaireGame(42, 3)
    const next = applyLocalSolitaireCommand(game, { type: 'draw' })
    expect(game.stock).toHaveLength(24)
    expect(next.stock).toHaveLength(21)
    expect(next.waste).toHaveLength(3)
    expect(next.moves).toBe(1)
  })

  it('sends an Ace to the first empty generic foundation', () => {
    const game = state({
      tableau: [[ace('hearts')], [], [], [], [], [], []],
      foundations: [[ace('clubs')], [], [], []],
    })
    const index = firstLegalFoundation(game, { zone: 'tableau', index: 0 }, 0)
    expect(index).toBe(1)
    const next = applyLocalSolitaireCommand(game, {
      type: 'move',
      from: { zone: 'tableau', index: 0 },
      startIndex: 0,
      to: { zone: 'foundation', index: index! },
    })
    expect(next.foundations[1]?.[0]).toMatchObject({ suit: 'hearts', rank: 1 })
    expect(next.score).toBe(10)
  })

  it('keeps an occupied foundation same-suit ascending', () => {
    const game = state({
      waste: [face('spades', 2)],
      foundations: [[ace('hearts')], [ace('spades')], [], []],
    })
    expect(() => applyLocalSolitaireCommand(game, {
      type: 'move',
      from: { zone: 'waste', index: 0 },
      startIndex: 0,
      to: { zone: 'foundation', index: 0 },
    })).toThrow(SolitaireRuleError)
    expect(applyLocalSolitaireCommand(game, {
      type: 'move',
      from: { zone: 'waste', index: 0 },
      startIndex: 0,
      to: { zone: 'foundation', index: 1 },
    }).foundations[1]).toHaveLength(2)
  })

  it('optimistically applies the same alternating tableau move and score', () => {
    const game = state({
      waste: [face('hearts', 12)],
      tableau: [[face('clubs', 13)], [], [], [], [], [], []],
    })
    const next = applyLocalSolitaireCommand(game, {
      type: 'move',
      from: { zone: 'waste', index: 0 },
      startIndex: 0,
      to: { zone: 'tableau', index: 0 },
    })
    expect(next.tableau[0]).toHaveLength(2)
    expect(next.score).toBe(5)
    expect(next.moves).toBe(1)
  })

  it('rejects an invalid click-pair locally without changing the board', () => {
    const game = state({ tableau: [[face('clubs', 8)], [face('hearts', 3)], [], [], [], [], []] })
    expect(canApplyLocalSolitaireCommand(game, {
      type: 'move',
      from: { zone: 'tableau', index: 0 },
      startIndex: 0,
      to: { zone: 'tableau', index: 1 },
    })).toBe(false)
  })

  it('auto-finishes exposed cards for draw-one and draw-three games', () => {
    for (const drawCount of [1, 3] as const) {
      const game = state({
        drawCount,
        waste: [face('hearts', 2)],
        foundations: [[ace('hearts')], [], [], []],
      })
      const result = autoFinishLocalSolitaire(game)
      expect(result.commands).toHaveLength(1)
      expect(result.game.foundations[0]).toHaveLength(2)
    }
  })
})

function state(overrides: Partial<SolitaireGame>): SolitaireGame {
  return {
    stock: [],
    waste: [],
    foundations: [[], [], [], []],
    tableau: [[], [], [], [], [], [], []],
    drawCount: 3,
    score: 0,
    moves: 0,
    message: 'Your move',
    ...overrides,
  }
}

function ace(suit: 'clubs' | 'diamonds' | 'hearts' | 'spades') {
  return face(suit, 1)
}

function face(
  suit: 'clubs' | 'diamonds' | 'hearts' | 'spades',
  rank: 1 | 2 | 3 | 8 | 12 | 13,
) {
  return { id: `${suit}-${rank}`, suit, rank, isFaceUp: true as const }
}

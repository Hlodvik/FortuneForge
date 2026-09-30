import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { PlayingCard } from './PlayingCard'
import type { CardSuit } from './cards'

describe('PlayingCard court faces', () => {
  it('uses one consistent court layout for every face rank and suit', () => {
    const suits: readonly CardSuit[] = ['clubs', 'diamonds', 'hearts', 'spades']
    const ranks = [11, 12, 13] as const

    for (const suit of suits) {
      for (const rank of ranks) {
        const markup = renderToStaticMarkup(createElement(PlayingCard, {
          card: { id: `${suit}-${rank}`, suit, rank },
        }))

        expect(markup).toContain('ff-playing-card__center ff-playing-card__center--court')
        expect(markup.match(/ff-playing-card__center--court/g)).toHaveLength(1)
      }
    }
  })

  it('keeps number-card centers out of the court layout', () => {
    const markup = renderToStaticMarkup(createElement(PlayingCard, {
      card: { id: 'diamonds-10', suit: 'diamonds', rank: 10 },
    }))

    expect(markup).not.toContain('ff-playing-card__center--court')
  })
})

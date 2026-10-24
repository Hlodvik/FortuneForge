import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { DefaultCardFace } from './DefaultCardFace'
import type { DragonTigerCard } from './contracts'

describe('Dragon Tiger standard standalone card faces', () => {
  it('marks fallback court faces separately from the host-owned shared renderer', () => {
    const suits: readonly DragonTigerCard['suit'][] = ['clubs', 'diamonds', 'hearts', 'spades']
    const ranks = [{ rank: 'jack', symbol: 'J' }, { rank: 'queen', symbol: 'Q' }, { rank: 'king', symbol: 'K' }] as const
    for (const suit of suits) {
      for (const { rank, symbol } of ranks) {
        const markup = renderToStaticMarkup(<DefaultCardFace card={{ rank, suit }}/>)
        expect(markup).toContain('ff-playing-card--standalone')
        expect(markup).toContain('ff-playing-card__center ff-playing-card__center--court')
        expect(markup.match(/ff-playing-card__center--court/g)).toHaveLength(1)
        expect(markup).toContain(`aria-hidden="true"><b>${symbol}</b><i>`)
        expect(markup).toContain('ff-playing-card__corner ff-playing-card__corner--bottom')
        expect(markup).toContain(`ff-playing-card--${suit === 'hearts' || suit === 'diamonds' ? 'red' : 'black'}`)
      }
    }
  })

  it('keeps number-card centers as suit symbols rather than court frames', () => {
    const markup = renderToStaticMarkup(<DefaultCardFace card={{ rank: 'ten', suit: 'diamonds' }}/>)
    expect(markup).toContain('aria-label="Ten of diamonds"')
    expect(markup).toContain('ff-playing-card__center" aria-hidden="true"><i>♦</i>')
    expect(markup).not.toContain('ff-playing-card__center--court')
  })

})

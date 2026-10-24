import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { JokerCard } from './JokerCard'
import { createShuffledDeck } from './cards'

describe('shared jester-hat joker', () => {
  it.each(['red', 'black'] as const)('uses the shared single-ink SVG style in %s', color => {
    const markup = renderToStaticMarkup(<JokerCard color={color} />)
    expect(markup).toContain(`ff-playing-card--${color}`)
    expect(markup).toContain('data-court-symbol="joker"')
    expect(markup).toContain('fill="currentColor"')
    expect(markup).not.toMatch(/<img|<image|href=/)
    expect(markup.match(/>JOKER</g)).toHaveLength(2)
  })
  it('keeps the symbol hidden for a face-down card', () => {
    const markup = renderToStaticMarkup(<JokerCard faceDown />)
    expect(markup).toContain('Face-down card')
    expect(markup).not.toContain('data-court-symbol')
  })
  it('does not change the standard 52-card games or add wild cards', () => {
    const deck = createShuffledDeck(24)
    expect(deck).toHaveLength(52)
    expect(deck.every(card => card.rank >= 1 && card.rank <= 13)).toBe(true)
  })
})

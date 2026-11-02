import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { StandardCardFace, type StandardCard } from './StandardCardFace.js'
import capturedFaces from './__fixtures__/standard-card-faces.json' with { type: 'json' }

// Captured from BOTH original game renderers before extraction, not generated
// from the shared implementation. Covers every rank/suit and exact DOM/ARIA.
const originalFaces = capturedFaces as { card: StandardCard; html: string }[]

describe('standalone card face compatibility', () => {
  it('covers the full standard deck without duplicate fixtures', () => {
    expect(originalFaces).toHaveLength(52)
    expect(new Set(originalFaces.map(({ card }) => `${card.rank}:${card.suit}`)).size).toBe(52)
  })

  it.each(originalFaces)('preserves the original $card.rank of $card.suit markup', ({ card, html }) => {
    expect(renderToStaticMarkup(<StandardCardFace card={card} />)).toBe(html)
  })
})

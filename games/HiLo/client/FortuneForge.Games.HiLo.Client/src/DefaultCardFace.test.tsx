import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { DefaultCardFace } from './DefaultCardFace'

describe('Hi-Lo standalone card face isolation', () => {
  it('marks fallback cards separately without changing number-card artwork', () => {
    const markup = renderToStaticMarkup(<DefaultCardFace card={{ rank: 'ten', suit: 'diamonds' }}/>)
    expect(markup).toContain('ff-playing-card--standalone')
    expect(markup).toContain('aria-label="Ten of diamonds"')
    expect(markup).toContain('ff-playing-card__center" aria-hidden="true"><i>♦</i>')
    expect(markup).not.toContain('ff-playing-card__center--court')
    expect(markup).toContain('ff-playing-card__corner ff-playing-card__corner--bottom')
  })

})

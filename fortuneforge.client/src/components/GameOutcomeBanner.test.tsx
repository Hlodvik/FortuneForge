import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { GameOutcomeBanner } from './GameOutcomeBanner'

describe('GameOutcomeBanner', () => {
  it('announces the result and the next action to assistive technology', () => {
    const markup = renderToStaticMarkup(createElement(GameOutcomeBanner, {
      label: 'Big win',
      title: '+R500',
      detail: 'Five matching symbols on line 1.',
      nextAction: 'Spin again when you are ready.',
      tone: 'win',
      significance: 'major',
    }))

    expect(markup).toContain('role="status"')
    expect(markup).toContain('aria-live="polite"')
    expect(markup).toContain('aria-atomic="true"')
    expect(markup).toContain('game-outcome-banner--win')
    expect(markup).toContain('game-outcome-banner--major')
    expect(markup).toContain('Spin again when you are ready.')
  })
})

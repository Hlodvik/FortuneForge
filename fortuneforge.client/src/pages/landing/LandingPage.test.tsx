import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'

vi.mock('../../features/account/useOptionalAccountSession', () => ({
  useOptionalAccountSession: () => ({ account: null, isLoading: false }),
}))

import { LandingPage } from './LandingPage'

describe('public landing page', () => {
  it('focuses on the game collection and never renders the Wukong mascot showcase', () => {
    const markup = renderToStaticMarkup(createElement(LandingPage))

    expect(markup).toContain('Find your fortune')
    expect(markup).toContain('slots, card tables, and arcade games')
    expect(markup).not.toContain('Wukong')
    expect(markup).not.toContain('landing-showcase')
  })
})

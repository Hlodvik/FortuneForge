import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({ hidden: [] as string[], loading: false, error: '', signedIn: false }))

vi.mock('../../features/account/useOptionalAccountSession', () => ({
  useOptionalAccountSession: () => ({ account: state.signedIn ? {} : null, isLoading: false }),
}))
vi.mock('../../features/catalog/catalogVisibility', () => ({
  useCatalogueVisibility: () => ({ loading: state.loading, error: state.error, isVisible: (route: string) => !state.hidden.includes(route) }),
}))

import { LandingPage } from './LandingPage'

describe('public landing page', () => {
  beforeEach(() => { state.hidden = []; state.loading = false; state.error = ''; state.signedIn = false })

  it('keeps the signed-in handoff lightweight while redirecting to home', () => {
    state.signedIn = true
    const markup = renderToStaticMarkup(createElement(LandingPage))
    expect(markup).toContain('class="ff-landing ff-landing--opening" role="status"')
    expect(markup).toContain('Opening your home…')
    expect(markup).not.toMatch(/<img|<video|ff-landing__hero/)
  })

  it('uses the approved dark hero and updated headline without How it works', () => {
    const markup = renderToStaticMarkup(createElement(LandingPage))

    expect(markup).toContain('Your next game')
    expect(markup).toContain('starts here.')
    expect(markup).toContain('fortune-forge-hero-v1.webp')
    expect(markup).toContain('fetchPriority="high"')
    expect(markup).not.toContain('How it works')
    expect(markup).not.toContain('A little instinct')
    expect(markup).toContain('href="/create-account"')
    expect(markup).toContain('href="/login"')
    expect(markup).toContain('href="#discover-games"')
    expect(markup).toContain('href="/home#home-lobby-games-title"')
    expect(markup).not.toContain('href="/games"')
    expect(markup).not.toContain('Wukong')
    expect(markup).not.toContain('landing-showcase')
    expect(markup).not.toContain('Play demo')
    expect(markup).not.toContain('How it works')
  })

  it('shows selected title artwork and functional category controls', () => {
    const markup = renderToStaticMarkup(createElement(LandingPage))
    expect(markup.match(/class="ff-landing__game-card"/g)).toHaveLength(4)
    expect(markup.match(/aria-pressed="/g)).toHaveLength(5)
    expect(markup).toContain('blackjack-title-v2.webp')
    expect(markup).toContain('dragon-tiger-title-v1.webp')
    expect(markup).toContain('mines-title-v2.webp')
    expect(markup).toContain('cosmic-fortune-title-v1.webp')
    expect(markup).toContain('loading="lazy" decoding="async"')
    expect(markup).not.toContain('player count')
  })

  it('honours catalogue hiding and fills the row from remaining verified games', () => {
    state.hidden = ['/games/mines', '/games/dragon-tiger']
    const markup = renderToStaticMarkup(createElement(LandingPage))
    expect(markup).not.toContain('href="/games/mines"')
    expect(markup).not.toContain('href="/games/dragon-tiger"')
    expect(markup).toContain('href="/games/baccarat"')
    expect(markup.match(/class="ff-landing__game-card"/g)).toHaveLength(4)
  })

  it('does not invent games when visibility is unavailable', () => {
    state.hidden = ['/cards/blackjack', '/games/dragon-tiger', '/games/mines', '/slots/cosmic-fortune', '/games/baccarat', '/games/roulette', '/games/craps', '/slots/rainbow-realm', '/games/2048', '/games/drop-merge']
    state.error = 'Offline'
    const markup = renderToStaticMarkup(createElement(LandingPage))
    expect(markup).toContain('Games could not be checked')
    expect(markup).not.toContain('class="ff-landing__game-card"')
  })
})

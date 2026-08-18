import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../../app/AppRoutes'
import { pageTitleForPath } from '../../app/usePageTitle'
import { CardGameLibraryPage } from './CardGameLibraryPage'

describe('card game route integration', () => {
  it('keeps the Blackjack demo as a lazy deep link', () => {
    expect(renderRoute('/demo/cards/blackjack')).toContain('Opening Fortune Forge…')
  })

  it('keeps Hold’em lazy in demo and authenticated rooms', () => {
    expect(renderRoute('/demo/cards/texas-holdem')).toContain('Opening Fortune Forge…')
    expect(renderRoute('/cards/texas-holdem')).toContain('Opening Fortune Forge…')
  })

  it('lazy-loads all account-neutral bot-practice labs under demo-only paths', () => {
    expect(renderRoute('/demo/cards/blackjack/bot-practice')).toContain('Opening Fortune Forge…')
    expect(renderRoute('/demo/cards/texas-holdem/bot-practice')).toContain('Opening Fortune Forge…')
    expect(renderRoute('/demo/cards/solitaire/bot-practice')).toContain('Opening Fortune Forge…')
  })

  it('lazy-loads gated Blackjack and competitive Solitaire wrappers', () => {
    expect(renderRoute('/cards/blackjack')).toContain('Opening Fortune Forge…')
    expect(renderRoute('/cards/solitaire')).toContain('Opening Fortune Forge…')
  })

  it('isolates authenticated Blackjack from the internal single-hand demo client', () => {
    const routeSource = source('../../app/routes/BlackjackRoute.tsx')
    const librarySource = source('../../app/routes/CardLibraryRoute.tsx')

    expect(routeSource).toContain('BlackjackTablePage')
    expect(routeSource).not.toContain("from '../../pages/cards/blackjack/BlackjackPage'")
    expect(librarySource).toContain('getBlackjackTableStatus')
    expect(librarySource).not.toContain('requestBlackjackStatus')
  })

  it('provides card-specific page titles', () => {
    expect(pageTitleForPath('/cards/blackjack')).toBe('Credit Blackjack Table — Fortune Forge')
    expect(pageTitleForPath('/demo/cards/blackjack')).toBe('Blackjack Demo — Fortune Forge')
    expect(pageTitleForPath('/cards/texas-holdem')).toBe('Credit Texas Hold’em — Fortune Forge')
    expect(pageTitleForPath('/cards/solitaire')).toBe('Competitive Solitaire — Fortune Forge')
    expect(pageTitleForPath('/demo/cards/blackjack/bot-practice')).toBe('Blackjack Practice Lab — Fortune Forge')
    expect(pageTitleForPath('/demo/cards/texas-holdem/bot-practice')).toBe('Texas Hold’em Practice Lab — Fortune Forge')
    expect(pageTitleForPath('/demo/cards/solitaire/bot-practice')).toBe('Solitaire Practice Lab — Fortune Forge')
  })

  it('keeps every card table above the shared cloud backdrop', () => {
    const shellSource = source('../../app/styles/shell.css')

    expect(shellSource).toContain('.app-shell > .blackjack-page,')
    expect(shellSource).toContain('.app-shell > .credit-holdem-page,')
    expect(shellSource).toContain('.app-shell > .holdem-page,')
    expect(shellSource).toContain('.app-shell > .solitaire-page,')
  })

  it('reserves a visible playing-card height for Solitaire controls', () => {
    const solitaireStyles = source('./solitaire/solitaire.css')

    expect(solitaireStyles).toMatch(/\.solitaire-card-button\s*\{[^}]*aspect-ratio:\s*0\.704;/s)
  })

  it('keeps every demo and practice table unlinked from the demo card library', () => {
    const markup = renderToStaticMarkup(createElement(CardGameLibraryPage, { demoMode: true }))

    expect(markup).toContain('href="/demo/cards" aria-current="page"')
    expect(markup).toContain('Fortune Blackjack')
    expect(markup).toContain('Texas Hold&#x27;em')
    expect(markup).toContain('Competitive Solitaire')
    expect(markup.match(/Internal preview/g)).toHaveLength(3)
    expect(markup).not.toContain('href="/demo/cards/blackjack"')
    expect(markup).not.toContain('href="/demo/cards/texas-holdem"')
    expect(markup).not.toContain('href="/demo/cards/solitaire"')
    expect(markup).not.toContain('bot-practice')
  })

  it('links all three authoritative credit games when their server gates are open', () => {
    const markup = renderToStaticMarkup(createElement(CardGameLibraryPage, {
      availability: {
        blackjack: 'available',
        texasHoldem: 'available',
        solitaire: 'available',
      },
    }))

    expect(markup).toContain('href="/cards/blackjack"')
    expect(markup).toContain('href="/cards/texas-holdem"')
    expect(markup).toContain('href="/cards/solitaire"')
    expect(markup.match(/Credit play available/g)).toHaveLength(3)
    expect(markup).not.toContain('bot-practice')
  })

  it('renders no game anchor when a server feature gate is closed or unresolved', () => {
    const markup = renderToStaticMarkup(createElement(CardGameLibraryPage, {
      availability: {
        blackjack: 'unavailable',
        texasHoldem: 'checking',
        solitaire: 'unavailable',
      },
    }))

    expect(markup).not.toContain('href="/cards/blackjack"')
    expect(markup).not.toContain('href="/cards/texas-holdem"')
    expect(markup).not.toContain('href="/cards/solitaire"')
    expect(markup).toContain('Checking server availability')
    expect(markup.match(/Credit table unavailable/g)).toHaveLength(2)
  })
})

function renderRoute(pathname: string): string {
  return renderToStaticMarkup(createElement(AppRoutes, {
    pathname,
    slotRoute: null,
    onSpinStateChange: () => undefined,
  }))
}

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}

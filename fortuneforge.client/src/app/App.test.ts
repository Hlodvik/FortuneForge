import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as slotRoutes from '../games/slots'
import { SLOT_ROUTE_DEFINITIONS } from '../games/slots/routeRegistry'
import App from './App'

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('app shell slot media isolation', () => {
  it('keeps cloud shell media out of current game and account routes', () => {
    expect(renderApp('/slots/wukong')).not.toContain('app-shell__background-video')
    expect(renderApp('/slots/rainbow-realm')).not.toContain('app-shell__background-video')
    expect(renderApp('/cards/texas-holdem')).not.toContain('app-shell__background-video')
    expect(renderApp('/account')).not.toContain('app-shell__background-video')
    expect(renderApp('/')).not.toContain('app-shell__background-video')
    expect(renderApp('/')).toContain('app-shell--landing')
  })

  it('permits legacy cloud shell media only for an explicit Wukong opt-in', () => {
    const wukong = SLOT_ROUTE_DEFINITIONS.find(route => route.id === 'wukong-journey-to-the-west')!
    const route = vi.spyOn(slotRoutes, 'findSlotRoute')
    route.mockReturnValue({ ...wukong, shellBackdrop: 'default-clouds' })
    expect(renderApp('/slots/wukong')).toContain('app-shell--wukong-clouds')
    expect(renderApp('/slots/wukong')).toContain('app-shell__background-video')

    route.mockReturnValue({ ...wukong, id: 'another-slot', shellBackdrop: 'default-clouds' })
    expect(renderApp('/slots/another-slot')).not.toContain('app-shell--wukong-clouds')
    expect(renderApp('/slots/another-slot')).not.toContain('app-shell__background-video')
  })

  it.each([
    '/home', '/home/history', '/home/settings', '/home/rand', '/home/credits',
    '/home/invoices', '/home/invoices/123', '/login', '/create-account', '/verify-email',
    '/admin', '/admin/operations', '/admin/invoices', '/admin/invoices/123',
    '/games', '/cards', '/slots', '/demo', '/demo/cards', '/unknown',
    ...SLOT_ROUTE_DEFINITIONS.flatMap(route => [route.playPath!, route.demoPath]),
  ])('does not inherit the old cloud background at %s', pathname => {
    const markup = renderApp(pathname)
    expect(markup).not.toContain('app-shell--wukong-clouds')
    expect(markup).not.toContain('app-shell__background-video')
    if (pathname === '/home' || pathname.startsWith('/home/')) {
      expect(markup).toContain('app-shell--home')
    }
  })

  it('uses a cloud-free dark default even after reduced-motion mascot styles load', () => {
    const shellStyles = readFileSync(new URL('./styles/shell.css', import.meta.url), 'utf8')
    const rootStyles = readFileSync(new URL('../index.css', import.meta.url), 'utf8')
    const mascotStyles = readFileSync(new URL('../games/slots/shared/mascot/mascotCompanion.css', import.meta.url), 'utf8')
    expect(shellStyles.match(/\.app-shell\s*\{([^}]*)\}/)?.[1]).toContain('background: var(--ff-home-background)')
    const defaultBackdrop = shellStyles.match(/\.app-shell::before\s*\{([^}]*)\}/)?.[1]
    expect(defaultBackdrop).toContain('background: none')
    expect(defaultBackdrop).toContain('content: none')
    expect(defaultBackdrop).not.toContain('url(')
    expect(shellStyles.match(/\.app-shell--wukong-clouds::before\s*\{([^}]*)\}/)?.[1]).toContain('background-clouds-poster.webp')
    expect(rootStyles).toMatch(/:root\s*\{[^}]*background: var\(--ff-home-background, #090a0d\)/)
    expect(rootStyles).toMatch(/html\s*\{[^}]*background: var\(--ff-home-background, #090a0d\)/)
    expect(mascotStyles).not.toContain('background-clouds-poster')
    expect(mascotStyles).not.toMatch(/\.app-shell::before\s*\{/)
  })

  it('preserves Wukong’s own cloud cabinet artwork and the other intentional slot themes', () => {
    const wukongTheme = readFileSync(new URL('../games/slots/wukong/cabinetTheme.ts', import.meta.url), 'utf8')
    expect(wukongTheme).toContain('neon-jewel-clouds-gold.png')
    expect(wukongTheme).toContain('pageBackdropImage: neonJewelCloudsGold')
    for (const route of SLOT_ROUTE_DEFINITIONS) {
      expect(route.shellBackdrop).toBe('theme')
    }
  })

  it('keeps Wukong and Nimbus artwork out of the home page', () => {
    const markup = renderApp('/')

    expect(markup).not.toContain('Wukong')
    expect(markup).not.toContain('Nimbus')
    expect(markup).not.toContain('mascot-companion')
  })

  it('uses the home background before the lazy landing and home pages have loaded', () => {
    expect(renderApp('/')).toContain('app-shell--landing')
    expect(renderApp('/home')).toContain('app-shell--home')
    expect(renderApp('/home/')).toContain('app-shell--home')
    expect(renderApp('/home')).not.toContain('app-shell__background-video')
    expect(renderApp('/slots/wukong')).not.toContain('app-shell--home')
    expect(renderApp('/cards/blackjack')).not.toContain('app-shell--home')

    const shellStyles = readFileSync(new URL('./styles/shell.css', import.meta.url), 'utf8')
    const homeStyles = readFileSync(new URL('../pages/account/homeLobby.css', import.meta.url), 'utf8')
    const landingStyles = readFileSync(new URL('../pages/landing/landingRedesign.css', import.meta.url), 'utf8')
    expect(shellStyles).toContain('--ff-home-background: #090a0d')
    expect(shellStyles).toMatch(/\.app-shell--landing,\s*\.app-shell--home\s*\{\s*background: var\(--ff-home-background\);/)
    expect(shellStyles).toMatch(/\.app-shell--landing::before,\s*\.app-shell--home::before\s*\{\s*background: none;\s*content: none;/)
    expect(homeStyles).toContain('background: var(--ff-home-background)')
    expect(landingStyles).toMatch(/\.ff-landing--opening\s*\{[^}]*background: var\(--ff-home-background\)/)
  })

  it('keeps the Asteroids route root above shell backdrops', () => {
    const shellStyles = readFileSync(new URL('./styles/shell.css', import.meta.url), 'utf8')

    expect(shellStyles).toContain('.app-shell > .asteroids-competition-page,')
  })

  it('marks playable routes without constraining game libraries or account pages', () => {
    expect(renderApp('/slots/pirates-fortune/demo')).toContain('app-shell--game')
    expect(renderApp('/games/keno')).toContain('app-shell--game')
    expect(renderApp('/cards/blackjack')).toContain('app-shell--game')
    expect(renderApp('/demo/cards/blackjack')).toContain('app-shell--game')

    expect(renderApp('/games')).not.toContain('app-shell--game')
    expect(renderApp('/cards')).not.toContain('app-shell--game')
    expect(renderApp('/demo/cards')).not.toContain('app-shell--game')
    expect(renderApp('/home')).not.toContain('app-shell--game')
  })

})

function renderApp(pathname: string): string {
  vi.stubGlobal('window', { location: { pathname } })
  vi.stubGlobal('document', { visibilityState: 'visible' })
  return renderToStaticMarkup(createElement(App))
}

import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Baccarat route integration', () => {
  it('lazy-registers the authenticated table ahead of shared game fallbacks', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./BaccaratRoute.tsx')

    expect(renderRoute('/games/baccarat')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/BaccaratRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/baccarat')")
    expect(routeSource).toContain("new HttpBaccaratGateway('/api/games/baccarat', fetchWithAccountSession)")
  })

  it('uses the canonical card faces and a table-only shell without catalogue clouds', () => {
    const routeSource = source('./BaccaratRoute.tsx')
    const shellStyles = source('./BaccaratRoute.css')
    expect(routeSource).toContain("import { PlayingCard } from '../../games/cards/shared/PlayingCard'")
    expect(routeSource).toContain('renderCardFace={renderCardFace}')
    expect(routeSource).toContain('className="baccarat-page"')
    expect(routeSource).not.toContain('className="player-page"')
    expect(routeSource).toContain('bodyClassName="baccarat-route__body"')
    expect(shellStyles).toContain('.app-shell:has(> .baccarat-page)::before { display: none; }')
    expect(shellStyles).toContain('overflow: hidden; background: #050a08;')
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

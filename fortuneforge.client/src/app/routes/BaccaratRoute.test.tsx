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

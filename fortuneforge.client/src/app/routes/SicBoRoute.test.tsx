import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Sic Bo route integration', () => {
  it('lazy-registers the authenticated table ahead of shared game fallbacks', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./SicBoRoute.tsx')

    const pending = renderRoute('/games/sic-bo')
    expect(pending).toContain('data-game-loading="sic-bo"')
    expect(pending).toContain('Loading Sic Bo…')
    expect(routesSource).toContain("import('./routes/SicBoRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/sic-bo')")
    expect(routeSource).toContain("new HttpSicBoGateway('/api/games/sic-bo', fetchWithAccountSession)")
  })
})

function renderRoute(pathname: string): string {
  return renderToStaticMarkup(createElement(AppRoutes, { pathname, slotRoute: null, onSpinStateChange: () => undefined }))
}

function source(relativePath: string): string { return readFileSync(new URL(relativePath, import.meta.url), 'utf8') }

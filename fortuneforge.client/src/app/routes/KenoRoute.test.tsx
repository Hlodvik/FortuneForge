import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Keno route integration', () => {
  it('lazy-registers the authenticated game ahead of shared game fallbacks', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./KenoRoute.tsx')

    expect(renderRoute('/games/keno')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/KenoRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/keno')")
    expect(routeSource).toContain("new HttpKenoGateway('/api/games/keno', fetchWithAccountSession)")
  })
})

function renderRoute(pathname: string): string {
  return renderToStaticMarkup(createElement(AppRoutes, { pathname, slotRoute: null, onSpinStateChange: () => undefined }))
}

function source(relativePath: string): string { return readFileSync(new URL(relativePath, import.meta.url), 'utf8') }

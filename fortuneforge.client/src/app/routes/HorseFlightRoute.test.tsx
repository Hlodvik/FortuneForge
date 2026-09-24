import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Horse Flight route integration', () => {
  it('lazy-registers the authenticated runner ahead of shared game fallbacks', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./HorseFlightRoute.tsx')

    expect(renderRoute('/games/horse-flight')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/HorseFlightRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/horse-flight')")
    expect(routeSource).toContain("new HttpHorseFlightGateway('/api/games/horse-flight', fetchWithAccountSession)")
    expect(routeSource).not.toContain('GameAmbientMusic')
  })
})

function renderRoute(pathname: string): string {
  return renderToStaticMarkup(createElement(AppRoutes, { pathname, slotRoute: null, onSpinStateChange: () => undefined }))
}

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}

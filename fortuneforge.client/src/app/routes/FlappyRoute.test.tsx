import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Flappy route integration', () => {
  it('lazy-registers the authenticated recorded Flappy room ahead of shared game fallbacks', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./FlappyRoute.tsx')

    expect(renderRoute('/games/flappy')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/FlappyRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/flappy')")
    expect(routeSource).toContain('new HttpArcadeCompetitionGateway(fetchWithAccountSession)')
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

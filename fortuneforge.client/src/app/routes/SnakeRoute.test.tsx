import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Snake route integration', () => {
  it('registers the authenticated arcade cabinet and uses the Snake package gateway', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./SnakeRoute.tsx')

    expect(renderRoute('/games/snake')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/SnakeRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/snake')")
    expect(routeSource).toContain('new LocalSnakeGateway()')
  })
})

function renderRoute(pathname: string): string {
  return renderToStaticMarkup(createElement(AppRoutes, { pathname, slotRoute: null, onSpinStateChange: () => undefined }))
}

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}

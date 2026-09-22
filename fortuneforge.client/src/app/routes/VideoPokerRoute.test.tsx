import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Video Poker route integration', () => {
  it('lazy-registers the authenticated table ahead of shared game fallbacks', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./VideoPokerRoute.tsx')

    expect(renderRoute('/games/video-poker')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/VideoPokerRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/video-poker')")
    expect(routeSource).toContain("new HttpVideoPokerGateway('/api/games/video-poker', fetchWithAccountSession)")
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

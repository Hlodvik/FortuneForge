import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

describe('Hearts route integration', () => {
  it('opens the implemented authenticated Hearts game, not a catalogue placeholder', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./HeartsRoute.tsx')

    expect(renderRoute('/cards/hearts')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/HeartsRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/cards/hearts')")
    expect(routeSource).toContain("new HttpHeartsGateway('/api/games/hearts')")
    expect(routeSource).toContain('<InGameShell account={account} title="Hearts" theme="cards"')
    expect(routeSource).toContain('<HeartsGame')
    expect(source('../../../../game-packages/games/Hearts/client/FortuneForge.Games.Hearts.Client/src/HeartsGame.tsx')).not.toContain('ff-hearts-header')
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

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
    expect(routeSource).toContain('new HttpArcadeCompetitionGateway(fetchWithAccountSession)')
    expect(routeSource).toContain('HorseFlightCompetitionGatewayAdapter')
    expect(routeSource).toContain('Play Free')
    expect(routeSource).toContain('Play Ranked · R1')
    expect(routeSource).toContain('View Leaderboard')
    expect(routeSource).toContain('Instructions')
    expect(routeSource).toContain('renderLaunchScreen={renderLaunchScreen}')
    expect(routeSource).toContain('terminalMessage')
    expect(routeSource).toContain('Score {score}')
    expect(routeSource).not.toContain('title="Horse Flight"')
    expect(routeSource).not.toContain('Score saved to the leaderboard')
    expect(routeSource).not.toContain('Run ended')
    expect(routeSource).not.toContain('You hit an obstacle')
    expect(routeSource).not.toContain('GameAmbientMusic')
  })
})

function renderRoute(pathname: string): string {
  return renderToStaticMarkup(createElement(AppRoutes, { pathname, slotRoute: null, onSpinStateChange: () => undefined }))
}

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}

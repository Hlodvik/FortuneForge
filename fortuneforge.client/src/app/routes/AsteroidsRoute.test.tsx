import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'
import { synchronizeAsteroidsRouteAccount } from './asteroidsRouteAccount'

describe('Asteroids route integration', () => {
  it('preserves a newer background account while the hook repeats its prior snapshot', () => {
    const staleAuthenticatedAccount = account(100)
    const refreshedAccount = account(900)

    expect(synchronizeAsteroidsRouteAccount(refreshedAccount, staleAuthenticatedAccount, false)).toBe(refreshedAccount)
    expect(synchronizeAsteroidsRouteAccount(refreshedAccount, staleAuthenticatedAccount, true)).toBe(staleAuthenticatedAccount)
  })

  it('lazy-registers the authenticated Asteroids room ahead of shared game fallbacks', () => {
    const routesSource = source('../AppRoutes.tsx')
    const routeSource = source('./AsteroidsRoute.tsx')

    expect(renderRoute('/games/asteroids')).toContain('Opening Fortune Forge…')
    expect(routesSource).toContain("import('./routes/AsteroidsRoute')")
    expect(routeSource).toContain("useAuthenticatedAccount('/games/asteroids')")
    expect(routeSource).toContain('new HttpArcadeCompetitionGateway(fetchWithAccountSession)')
  })
})

function account(slotsCredits: number) {
  return {
    userId: 'player-1',
    playerName: 'Pilot',
    email: 'pilot@example.test',
    createdAtUtc: '2026-09-05T00:00:00Z',
    balances: { slotsCredits, freeGames: 0 },
    slots: {
      spinsPlayed: 0,
      wins: 0,
      losses: 0,
      creditsWagered: 0,
      creditsWon: 0,
      netCredits: 0,
    },
    role: 'Player',
  }
}

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

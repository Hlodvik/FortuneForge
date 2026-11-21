// @vitest-environment jsdom
import { readFileSync } from 'node:fs'
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import type { HttpArcadeCompetitionGateway } from '../../games/arcade/arcadeCompetitionApi'
import { AppRoutes } from '../AppRoutes'
import { AuthenticatedAsteroidsRoute } from './AsteroidsRoute'
import { synchronizeAsteroidsRouteAccount } from './asteroidsRouteAccount'

const session = vi.hoisted(() => ({
  account: null as AccountSummary | null,
  gateway: null as HttpArcadeCompetitionGateway | null,
}))

vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({ account: session.account, error: null, isLoading: false, reload: vi.fn() }),
}))
vi.mock('../../pages/games/asteroids/AsteroidsCompetitionPage', () => ({
  AsteroidsCompetitionPage: ({ gateway }: { gateway: HttpArcadeCompetitionGateway }) => {
    session.gateway = gateway
    return null
  },
}))
vi.mock('../../features/audio/GameAmbientMusic', () => ({ GameAmbientMusic: () => null }))
vi.mock('../../components/InGameShell', () => ({ InGameShell: ({ children }: { children: React.ReactNode }) => children }))

afterEach(() => {
  vi.unstubAllGlobals()
  sessionStorage.clear()
  localStorage.clear()
  session.gateway = null
  session.account = null
})

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
  })

  it('keeps an original paid intent bound to its account through bearer and cookie fallback after a route account switch', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
    const transport = vi.fn().mockImplementation(async (_input, init: RequestInit) =>
      Response.json({}, { status: new Headers(init.headers).has('Authorization') ? 401 : 403 }))
    vi.stubGlobal('fetch', transport)
    sessionStorage.setItem('fortune-forge.account-token', 'a'.repeat(32))
    localStorage.setItem('fortune-forge.account-token', 'b'.repeat(32))
    session.account = account(100)
    const host = document.createElement('div')
    const root = createRoot(host)
    try {
      await act(async () => { root.render(createElement(AuthenticatedAsteroidsRoute)) })
      const originalGateway = session.gateway!
      session.account = account(900)
      await act(async () => { root.render(createElement(AuthenticatedAsteroidsRoute)) })
      expect(session.gateway).toBe(originalGateway)
      session.account = { ...account(200), userId: 'player-2' }
      await act(async () => { root.render(createElement(AuthenticatedAsteroidsRoute)) })
      const replacementGateway = session.gateway!
      const admission = {
        expectedWindowStartsAtUtc: '2026-11-21T06:00:00Z',
        expectedWindowEndsAtUtc: '2026-11-22T06:00:00Z',
      }
      await expect(originalGateway.startAsteroidsAttempt('daily', 'original-paid-intent', undefined,
        { ...admission, recoverOnly: true }))
        .rejects.toMatchObject({ status: 403 })
      expect(transport).toHaveBeenCalledTimes(3)
      for (const [url, init] of transport.mock.calls as [string, RequestInit][]) {
        const headers = new Headers(init.headers)
        expect(url).toBe('/api/arcade-competitions/asteroids/daily/attempts?recoverOnly=true')
        expect(headers.get('X-FortuneForge-Expected-Account')).toBe('player-1')
        expect(headers.get('Idempotency-Key')).toBe('original-paid-intent')
        expect(init.body).toBe(JSON.stringify(admission))
      }
      expect(transport.mock.calls.map(([, init]) => new Headers(init.headers).get('Authorization')))
        .toEqual([`Bearer ${'a'.repeat(32)}`, `Bearer ${'b'.repeat(32)}`, null])
      transport.mockClear()
      await expect(replacementGateway.startAsteroidsAttempt('daily', 'replacement-paid-intent', undefined, admission))
        .rejects.toMatchObject({ status: 403 })
      expect(transport.mock.calls.every(([, init]) =>
        new Headers(init.headers).get('X-FortuneForge-Expected-Account') === 'player-2')).toBe(true)
    } finally {
      await act(async () => { root.unmount() })
    }
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

// @vitest-environment jsdom
import { createElement, type ComponentType, type ReactNode } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { GameLoadingRouteProvider } from '../../features/game-loading/GameLoadingRouteContext'
import { resolveGameLoadingEntry } from '../../features/game-loading/gameLoadingRegistry'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { SiteNavbar } from '../../components/SiteNavbar'
import appRoutesSource from '../AppRoutes.tsx?raw'

type SmokeInventory = {
  nonSlotSmokeInventory: readonly { smokePath: string; catalogPath?: string }[]
  slotSmokeInventory: readonly (readonly [string, string])[]
}
const { nonSlotSmokeInventory, slotSmokeInventory } = import.meta.glob<SmokeInventory>('../../../../scripts/game-smoke-inventory.mjs', { eager: true })['../../../../scripts/game-smoke-inventory.mjs']

const state = vi.hoisted(() => ({ account: null as AccountSummary | null, loading: false, error: null as string | null }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: state.account, isLoading: state.loading, error: state.error, reload: () => undefined,
}) }))
// SSR does not run game effects or make wagers; retain any visual portal content in its host.
vi.mock('react-dom', async importOriginal => ({
  ...await importOriginal<typeof import('react-dom')>(), createPortal: (children: ReactNode) => children,
}))

const account: AccountSummary = {
  userId: 'all-game-navbar', playerName: 'Navbar Player', email: 'navbar@example.test', role: 'player',
  createdAtUtc: '2026-11-01T00:00:00Z', balances: { slotsCredits: 1234.56, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
const routeModules = import.meta.glob('./*Route.tsx')
const lazyEntries = new Map(appRoutesSource.split('\n').flatMap(line => {
  const match = line.match(/^const (\w+) = lazy\(.*import\(['"]([^'"]+)['"]\).*default:\s*module\.(\w+)/)
  return match ? [[match[1], { module: match[2].replace('./routes/', './') + '.tsx', exported: match[3] }] as const] : []
}))
const productionRoutes = nonSlotSmokeInventory.map((entry: { smokePath: string; catalogPath?: string }) => {
  const path = entry.catalogPath ?? entry.smokePath
  const escaped = path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const component = appRoutesSource.match(new RegExp(`pathname\\s*===\\s*['"]${escaped}['"]\\)\\s*route\\s*=\\s*<(\\w+)\\b`))?.[1]
  return { path, component, entry: component ? lazyEntries.get(component) : undefined }
})

beforeEach(() => { state.account = account; state.loading = false; state.error = null })
afterEach(() => window.history.replaceState(null, '', '/'))

function hostFor(node: ReactNode) {
  const host = document.createElement('div')
  host.innerHTML = renderToStaticMarkup(node)
  return host
}

describe('all registered production games share canonical navigation', () => {
  it('covers every non-slot entry and every registered slot route without a per-game navbar exemption', () => {
    expect(productionRoutes).toHaveLength(25)
    expect(new Set(productionRoutes.map(route => route.path)).size).toBe(25)
    for (const route of productionRoutes) {
      expect(route.entry, route.path).toBeDefined()
      expect(routeModules[route.entry!.module], route.path).toBeTypeOf('function')
    }
    expect(slotSmokeInventory).toHaveLength(20)
    expect(new Set(SLOT_ROUTE_DEFINITIONS.map(route => route.playPath))).toEqual(new Set(slotSmokeInventory.map(([route]) => `/slots/${route}`)))
  })

  describe.each(['ready', 'loading', 'error'] as const)('%s account boundary', phase => {
    it.each(productionRoutes)('$path renders the real route with one shared navbar', async route => {
      window.history.replaceState(null, '', route.path)
      if (phase !== 'ready') { state.account = null; state.loading = phase === 'loading'; state.error = phase === 'error' ? 'Please sign in again.' : null }
      const module = await routeModules[route.entry!.module]() as Record<string, ComponentType>
      const Page = module[route.entry!.exported]
      const host = hostFor(<GameLoadingRouteProvider entry={resolveGameLoadingEntry(route.path)}>{createElement(Page)}</GameLoadingRouteProvider>)
      const navbars = host.querySelectorAll('[data-site-navbar]')
      expect(navbars).toHaveLength(1)
      const navbar = navbars[0]
      expect(navbar.closest('[data-game-surface]')).toBeNull()
      expect(navbar.getAttribute('data-game-navbar')).toBe('true')
      const expected = hostFor(<SiteNavbar mode="game" authenticated account={phase === 'ready' ? account : undefined} />).querySelector('[data-site-navbar]')!
      expect(navbar.outerHTML).toBe(expected.outerHTML)
      expect(navbar.querySelector('[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/home')
      expect(navbar.querySelector('.player-shell-header__games')?.getAttribute('href')).toBe('/home')
      expect(navbar.querySelectorAll('.site-header-balance')).toHaveLength(phase === 'ready' ? 1 : 0)
      expect(navbar.querySelector('[aria-label="Account menu"]') !== null).toBe(phase === 'ready')
    })
  })
})

// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { GameLoadingRouteProvider } from '../../features/game-loading/GameLoadingRouteContext'
import { resolveGameLoadingEntry } from '../../features/game-loading/gameLoadingRegistry'
import { findSlotRoute } from '../../games/slots/routeRegistry'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { isGameUnderMaintenance } from '../../features/catalog/gameMaintenance'

const state = vi.hoisted(() => ({ account: null as AccountSummary | null, loading: true,
  error: null as string | null, auth: vi.fn(), reload: vi.fn(), pages: [] as unknown[] }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: (path: string) => {
  state.auth(path)
  return { account: state.account, isLoading: state.loading, error: state.error, reload: state.reload }
} }))
vi.mock('./ArcaneArchivesHost', () => ({ ArcaneArchivesHost: (props: unknown) => {
  state.pages.push(props)
  return <main data-owned-slot="arcane-archives">Owned Arcane</main>
} }))
vi.mock('./CrookedRoadHost', () => ({ CrookedRoadHost: (props: unknown) => {
  state.pages.push(props)
  return <main data-owned-slot="crooked-road">Owned Crooked Road</main>
} }))
vi.mock('./SunstoneEclipseHost', () => ({ SunstoneEclipseHost: (props: unknown) => {
  state.pages.push(props)
  return <main data-owned-slot="robot-revolution">Owned Sunstone Eclipse</main>
} }))
import { SlotGameRoute } from './SlotGameRoute'

const account: AccountSummary = {
  userId: 'native-route-player', playerName: 'Local Player', email: 'local@example.test',
  createdAtUtc: '2026-11-22T00:00:00Z', role: 'player', balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  state.account = null; state.loading = true; state.error = null; state.pages = []
  state.auth.mockReset(); state.reload.mockReset()
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })

async function renderRoute(path: string) {
  const definition = findSlotRoute(path)!
  const load = vi.fn(definition.load)
  await act(async () => root.render(<GameLoadingRouteProvider entry={resolveGameLoadingEntry(path)}>
    <SlotGameRoute definition={{ ...definition, load }} pathname={path} />
  </GameLoadingRouteProvider>))
  return load
}

it('waits for authentication and mounts Arcane without requesting a legacy manifest', async () => {
  const load = await renderRoute('/slots/arcane-archives')
  expect(state.auth).toHaveBeenCalledWith('/slots/arcane-archives')
  expect(state.pages).toHaveLength(0)
  expect(load).not.toHaveBeenCalled()
  state.account = account; state.loading = false
  await renderRoute('/slots/arcane-archives')
  expect(host.querySelector('[data-owned-slot="arcane-archives"]')).not.toBeNull()
  expect(state.pages.at(-1)).toMatchObject({ account })
})

it('preserves other package-owned routes', async () => {
  state.account = account; state.loading = false
  const load = await renderRoute('/slots/crooked-road')
  expect(host.querySelector('[data-owned-slot="crooked-road"]')).not.toBeNull()
  expect(load).not.toHaveBeenCalled()
})
it('mounts Sunstone through its package host after authentication, without the retired manifest runtime', async () => {
  state.account = account; state.loading = false
  const load = await renderRoute('/slots/sunstone-eclipse')
  expect(host.querySelector('[data-owned-slot="robot-revolution"]')).not.toBeNull()
  expect(load).not.toHaveBeenCalled()
  expect(state.pages.at(-1)).toMatchObject({ account })
})

it('blocks a maintenance title before loading a manifest, authentication or a game runtime', async () => {
  const paused = SLOT_ROUTE_DEFINITIONS.filter(isGameUnderMaintenance)
  expect(paused).toHaveLength(10)
  expect(paused.some(game => game.id === 'robot-revolution')).toBe(false)
  for (const definition of paused) {
    const load = await renderRoute(definition.playPath!)
    expect(host.querySelector('[role="status"]')?.textContent).toContain('Under Maintenance')
    expect(load).not.toHaveBeenCalled()
  }
  expect(state.auth).not.toHaveBeenCalled()
  expect(state.pages).toHaveLength(0)
})

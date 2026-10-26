// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { GameLoadingRouteProvider } from '../../features/game-loading/GameLoadingRouteContext'
import { resolveGameLoadingEntry } from '../../features/game-loading/gameLoadingRegistry'
import type { SlotRouteDefinition } from '../../games/slots/routeRegistry'
import type { SlotGameManifest } from '../../games/slots/shared/slotGameManifest'

type SlotPageProps = {
  account?: AccountSummary
  demoMode?: boolean
  experienceSet: SlotGameManifest['experience']
  onSpinStateChange: (spinning: boolean) => void
}
const state = vi.hoisted(() => ({
  account: null as AccountSummary | null,
  loading: true,
  error: null as string | null,
  auth: vi.fn(), reload: vi.fn(), pages: [] as SlotPageProps[],
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: (returnPath: string) => {
    state.auth(returnPath)
    return { account: state.account, isLoading: state.loading, error: state.error, reload: state.reload }
  },
}))
vi.mock('../../pages/slots/SlotsPage', () => ({
  SlotsPage: (props: SlotPageProps) => {
    state.pages.push(props)
    return <main data-slot-ready={props.experienceSet.id} data-demo={props.demoMode ? 'true' : 'false'}>Slot ready</main>
  },
}))

import { SlotGameRoute } from './SlotGameRoute'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((accept, fail) => { resolve = accept; reject = fail })
  return { promise, resolve, reject }
}
function definition(id: 'wukong' | 'rainbow-realm', load: () => Promise<SlotGameManifest>): SlotRouteDefinition {
  const title = id === 'wukong' ? "Wukong's Journey to the West" : 'Rainbow Realm'
  return {
    id: id === 'wukong' ? 'wukong-journey-to-the-west' : id,
    title, shortTitle: title, playPath: `/slots/${id}`, demoPath: `/slots/${id}/demo`,
    serverGameIds: [`${id}-fixture`], shellBackdrop: 'theme', load,
  }
}
function manifest(route: SlotRouteDefinition): SlotGameManifest {
  return {
    id: route.id, routes: { play: route.playPath, demo: route.demoPath },
    catalog: { id: route.id, title: route.title, shortTitle: route.shortTitle, description: 'Test fixture', image: '/fixture-art.webp', imagePresentation: 'cover' },
    // The page is mocked; no full playable experience or game package is loaded.
    experience: { id: `${route.id}-fixture` } as SlotGameManifest['experience'],
  }
}
const account: AccountSummary = {
  userId: 'loading-test-player', playerName: 'Loading test player', email: 'loading@example.test',
  createdAtUtc: '2026-10-26T00:00:00Z', role: 'player',
  balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
const spinChanged = vi.fn()
let host: HTMLDivElement
let root: Root | null
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  state.account = null; state.loading = true; state.error = null; state.pages = []
  state.auth.mockReset(); state.reload.mockReset(); spinChanged.mockReset()
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => {
  if (root) act(() => root!.unmount())
  host.remove(); vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals()
})
const renderRoute = (route: SlotRouteDefinition, pathname = route.playPath!) => act(async () => {
  root!.render(<GameLoadingRouteProvider entry={resolveGameLoadingEntry(pathname)}>
    <SlotGameRoute definition={route} pathname={pathname} onSpinStateChange={spinChanged} />
  </GameLoadingRouteProvider>)
})
const loadingId = () => host.querySelector('[data-game-loading]')?.getAttribute('data-game-loading')

describe('slot manifest and account loading presentations', () => {
  it('shows the real manifest and account pending phases, then reveals the game immediately when both are ready', async () => {
    vi.useFakeTimers()
    const gate = deferred<SlotGameManifest>()
    const route = definition('wukong', vi.fn(() => gate.promise))
    await renderRoute(route)
    expect(route.load).toHaveBeenCalledOnce()
    expect(loadingId()).toBe('wukong-journey-to-the-west')
    expect(state.auth).not.toHaveBeenCalled()
    expect(state.pages).toHaveLength(0)

    const loaded = manifest(route)
    await act(async () => { gate.resolve(loaded) })
    expect(loadingId()).toBe(route.id)
    expect(state.auth).toHaveBeenCalledWith(route.playPath)
    expect(state.pages).toHaveLength(0)

    state.account = account; state.loading = false
    await renderRoute(route)
    expect(host.querySelector('[data-slot-ready]')?.getAttribute('data-slot-ready')).toBe(loaded.experience.id)
    expect(host.querySelector('[data-game-loading], [role="status"]')).toBeNull()
    expect(state.pages.at(-1)).toMatchObject({ account, experienceSet: loaded.experience, onSpinStateChange: spinChanged })
    expect(vi.getTimerCount()).toBe(0)
  })

  it('preserves manifest failure details, return-home and real retry instead of masking them with artwork', async () => {
    const failed = deferred<SlotGameManifest>()
    const retried = deferred<SlotGameManifest>()
    const load = vi.fn<() => Promise<SlotGameManifest>>().mockReturnValueOnce(failed.promise).mockReturnValueOnce(retried.promise)
    const route = definition('rainbow-realm', load)
    await renderRoute(route)
    await act(async () => { failed.reject(new Error('The manifest download failed.')) })
    const alert = host.querySelector('[role="alert"]')!
    expect(alert.textContent).toContain('Rainbow Realm could not be opened.')
    expect(alert.textContent).toContain('The manifest download failed.')
    expect(alert.querySelector('a')?.getAttribute('href')).toBe('/home')
    expect(alert.querySelector('a')?.textContent).toBe('Return home')
    expect(loadingId()).toBeUndefined()
    expect(state.auth).not.toHaveBeenCalled()
    expect(state.pages).toHaveLength(0)

    await act(async () => { alert.querySelector<HTMLButtonElement>('button')!.click() })
    expect(load).toHaveBeenCalledTimes(2)
    expect(host.querySelector('[role="alert"]')).toBeNull()
    expect(loadingId()).toBe(route.id)
    state.account = account; state.loading = false
    await act(async () => { retried.resolve(manifest(route)) })
    expect(host.textContent).toBe('Slot ready')
    expect(loadingId()).toBeUndefined()
  })

  it('keeps an account error and its original reload action visible after the manifest loads', async () => {
    const gate = deferred<SlotGameManifest>()
    const route = definition('wukong', () => gate.promise)
    await renderRoute(route)
    state.error = 'Your account could not be checked.'; state.loading = false
    await act(async () => { gate.resolve(manifest(route)) })
    const alert = host.querySelector('[role="alert"]')!
    expect(alert.textContent).toContain('Fortune Slots could not be opened.')
    expect(alert.textContent).toContain(state.error)
    expect(alert.querySelector('a')?.getAttribute('href')).toBe('/home')
    expect(loadingId()).toBeUndefined()
    expect(state.pages).toHaveLength(0)
    act(() => alert.querySelector<HTMLButtonElement>('button')!.click())
    expect(state.reload).toHaveBeenCalledOnce()
  })

  it('loads a real demo manifest without invoking account authentication', async () => {
    const gate = deferred<SlotGameManifest>()
    const route = definition('rainbow-realm', () => gate.promise)
    state.error = 'An unrelated signed-out account state'
    await renderRoute(route, route.demoPath)
    expect(loadingId()).toBe(route.id)
    const loaded = manifest(route)
    await act(async () => { gate.resolve(loaded) })
    expect(state.auth).not.toHaveBeenCalled()
    expect(host.querySelector('[data-slot-ready]')?.getAttribute('data-demo')).toBe('true')
    expect(state.pages.at(-1)).toMatchObject({ demoMode: true, experienceSet: loaded.experience, onSpinStateChange: spinChanged })
    expect(state.pages.at(-1)?.account).toBeUndefined()
    expect(loadingId()).toBeUndefined()
  })

  it.each(['resolve', 'reject'] as const)('ignores a stale manifest %s after the definition changes', async outcome => {
    const old = deferred<SlotGameManifest>(), current = deferred<SlotGameManifest>()
    const previousRoute = definition('wukong', () => old.promise)
    const currentRoute = definition('rainbow-realm', () => current.promise)
    await renderRoute(previousRoute)
    await renderRoute(currentRoute)
    await act(async () => {
      if (outcome === 'resolve') old.resolve(manifest(previousRoute))
      else old.reject(new Error('Stale download failed'))
    })
    expect(loadingId()).toBe(currentRoute.id)
    expect(host.querySelector('[role="alert"]')).toBeNull()
    expect(state.auth).not.toHaveBeenCalled()
    expect(state.pages).toHaveLength(0)
    state.account = account; state.loading = false
    await act(async () => { current.resolve(manifest(currentRoute)) })
    expect(host.querySelector('[data-slot-ready]')?.getAttribute('data-slot-ready')).toBe(`${currentRoute.id}-fixture`)
    expect(state.pages.every(page => page.experienceSet.id === `${currentRoute.id}-fixture`)).toBe(true)
  })

  it.each(['resolve', 'reject'] as const)('ignores a manifest %s after unmounting', async outcome => {
    const gate = deferred<SlotGameManifest>()
    const route = definition('wukong', () => gate.promise)
    await renderRoute(route)
    act(() => root!.unmount()); root = null
    await act(async () => {
      if (outcome === 'resolve') gate.resolve(manifest(route))
      else gate.reject(new Error('Unmounted download failed'))
    })
    expect(host.textContent).toBe('')
    expect(state.auth).not.toHaveBeenCalled()
    expect(state.pages).toHaveLength(0)
  })
})

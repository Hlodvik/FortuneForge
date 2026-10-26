// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useGameLoadingEntry } from '../features/game-loading/gameLoadingContext'
import { resolveGameLoadingEntry } from '../features/game-loading/gameLoadingRegistry'
import type { SlotRouteDefinition } from '../games/slots/routeRegistry'

const chunks = vi.hoisted(() => {
  let resolveBlackjack!: (module: { AuthenticatedBlackjackRoute: () => ReactNode }) => void
  let resolveSlot!: (module: { SlotGameRoute: () => ReactNode }) => void
  return {
    blackjack: new Promise<{ AuthenticatedBlackjackRoute: () => ReactNode }>((resolve) => { resolveBlackjack = resolve }),
    slot: new Promise<{ SlotGameRoute: () => ReactNode }>((resolve) => { resolveSlot = resolve }),
    login: new Promise<never>(() => {}),
    unknown: new Promise<never>(() => {}),
    finishBlackjack: (module: { AuthenticatedBlackjackRoute: () => ReactNode }) => resolveBlackjack(module),
    finishSlot: (module: { SlotGameRoute: () => ReactNode }) => resolveSlot(module),
  }
})

vi.mock('./routes/BlackjackRoute', () => chunks.blackjack)
vi.mock('./routes/SlotGameRoute', () => chunks.slot)
vi.mock('../pages/auth/LoginPage', () => chunks.login)
vi.mock('../pages/not-found/NotFoundPage', () => chunks.unknown)
vi.mock('./routes/HeartsRoute', () => ({
  AuthenticatedHeartsRoute: () => { throw new Error('Expected route render failure') },
}))

import { AppRoutes } from './AppRoutes'

let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

const renderRoute = (pathname: string, slotRoute: SlotRouteDefinition | null = null) => act(async () => {
  root.render(<AppRoutes pathname={pathname} slotRoute={slotRoute} onSpinStateChange={() => undefined} />)
})

describe('game route loading integration', () => {
  it('shows recognised game artwork only while its lazy chunk is pending, then renders ready without a timer', async () => {
    vi.useFakeTimers()
    await renderRoute('/cards/blackjack')
    const entry = resolveGameLoadingEntry('/cards/blackjack')!
    expect(host.querySelector('[data-game-loading]')?.getAttribute('data-game-loading')).toBe('blackjack')
    expect(host.querySelector('img')?.getAttribute('src')).toBe(entry.image)
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Loading Fortune Blackjack…')
    expect(host.querySelector('[data-route-ready]')).toBeNull()

    function ReadyBlackjack() {
      return <main data-route-ready="blackjack" data-route-entry={useGameLoadingEntry()?.id}>Blackjack ready</main>
    }
    await act(async () => { chunks.finishBlackjack({ AuthenticatedBlackjackRoute: ReadyBlackjack }) })
    expect(host.querySelector('[data-route-ready]')?.getAttribute('data-route-entry')).toBe('blackjack')
    expect(host.textContent).toBe('Blackjack ready')
    expect(host.querySelector('[data-game-loading], [role="status"]')).toBeNull()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('resolves a slot loading presentation before the lazy slot wrapper has loaded', async () => {
    const definition: SlotRouteDefinition = {
      id: 'cosmic-fortune', title: 'Cosmic Fortune', shortTitle: 'Cosmic Fortune',
      playPath: '/slots/cosmic-fortune', demoPath: '/slots/cosmic-fortune/demo',
      serverGameIds: ['cosmic-fortune-v1'], shellBackdrop: 'theme', load: vi.fn(),
    }
    await renderRoute('/slots/cosmic-fortune', definition)
    expect(host.querySelector('[data-game-loading]')?.getAttribute('data-game-loading')).toBe('cosmic-fortune')
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Loading Cosmic Fortune…')
    expect(definition.load).not.toHaveBeenCalled()
    await act(async () => { chunks.finishSlot({ SlotGameRoute: () => <main>Slot wrapper ready</main> }) })
    expect(host.textContent).toBe('Slot wrapper ready')
    expect(host.querySelector('[data-game-loading]')).toBeNull()
  })

  it.each(['/login', '/a-route-that-does-not-exist'])('retains the existing generic Suspense fallback for %s', async pathname => {
    await renderRoute(pathname)
    expect(host.querySelector('main.route-state[role="status"]')?.textContent).toBe('Opening Fortune Forge…')
    expect(host.querySelector('[data-game-loading], img')).toBeNull()
  })

  it('keeps the error boundary alert, reload and return-home controls instead of a loading screen', async () => {
    const reported = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    await renderRoute('/cards/hearts')
    const alert = host.querySelector('main.route-state--error[role="alert"]')!
    expect(alert.textContent).toContain('This page could not be loaded.')
    expect(alert.textContent).toContain('Check your connection, then try loading the page again.')
    expect(alert.querySelector('a')?.getAttribute('href')).toBe('/')
    expect(alert.querySelector('a')?.textContent).toBe('Return to Fortune Forge')
    expect(host.querySelector('[data-game-loading], [role="status"]')).toBeNull()
    expect(reported).toHaveBeenCalled()

    const reload = vi.fn()
    const browserWindow = window
    vi.stubGlobal('window', new Proxy(browserWindow, {
      get(target, key) { return key === 'location' ? { reload } : Reflect.get(target, key, target) },
    }))
    act(() => alert.querySelector<HTMLButtonElement>('button')!.click())
    expect(reload).toHaveBeenCalledOnce()
  })
})

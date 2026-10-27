// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SlotRouteDefinition } from '../games/slots'

const state = vi.hoisted(() => ({ replace: vi.fn(), legacyLoads: vi.fn() }))
vi.mock('./routes/SlotsLibraryRoute', () => {
  state.legacyLoads('slots')
  return { AuthenticatedSlotsLibraryRoute: () => <main>Old slots catalogue</main> }
})
vi.mock('./routes/CardLibraryRoute', () => {
  state.legacyLoads('cards')
  return { AuthenticatedCardLibraryRoute: () => <main>Old cards catalogue</main> }
})
vi.mock('./routes/GameHubRoute', () => {
  state.legacyLoads('games')
  return { AuthenticatedGameHubRoute: () => <main>Old games catalogue</main> }
})
vi.mock('./routes/BlackjackRoute', () => ({ AuthenticatedBlackjackRoute: () => <main>Blackjack table</main> }))
vi.mock('./routes/DragonTigerRoute', () => ({ AuthenticatedDragonTigerRoute: () => <main>Dragon Tiger table</main> }))
vi.mock('./routes/SlotGameRoute', () => ({ SlotGameRoute: () => <main>Slot game</main> }))
vi.mock('../pages/slots/SlotsLibraryPage', () => ({ DemoSlotsLibraryPage: () => <main>Public slot demos</main> }))
vi.mock('../pages/cards/CardGameLibraryPage', () => ({ CardGameLibraryPage: () => <main>Public card demos</main> }))

import { AppRoutes } from './AppRoutes'

let host: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  state.replace.mockReset()
  state.legacyLoads.mockClear()
  const browserWindow = window
  vi.stubGlobal('window', new Proxy(browserWindow, {
    get(target, key) {
      return key === 'location' ? { replace: state.replace } : Reflect.get(target, key, target)
    },
  }))
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

const renderRoute = (pathname: string, slotRoute: SlotRouteDefinition | null = null) => act(async () => {
  root.render(<AppRoutes pathname={pathname} slotRoute={slotRoute} onSpinStateChange={() => undefined} />)
})

describe('home replaces separate authenticated catalogues', () => {
  it.each([
    ['/slots', '/home?category=Slots#home-lobby-games-title'],
    ['/cards', '/home?category=Cards#home-lobby-games-title'],
    ['/games', '/home#home-lobby-games-title'],
  ])('replaces %s with the home catalogue without loading the old page', async (pathname, destination) => {
    await renderRoute(pathname)
    expect(state.replace).toHaveBeenCalledExactlyOnceWith(destination)
    expect(state.legacyLoads).not.toHaveBeenCalled()
    expect(host.querySelector('[role="status"]')?.textContent).toBe('Opening your home…')
    expect(host.querySelector('img, video, [data-game-loading]')).toBeNull()
    expect(host.textContent).not.toContain('Old')
  })

  it.each([
    ['/cards/blackjack', 'Blackjack table'],
    ['/games/dragon-tiger', 'Dragon Tiger table'],
    ['/demo', 'Public slot demos'],
    ['/demo/cards', 'Public card demos'],
  ])('leaves %s available without a catalogue redirect', async (pathname, content) => {
    await renderRoute(pathname)
    expect(host.textContent).toBe(content)
    expect(state.replace).not.toHaveBeenCalled()
  })

  it('leaves a concrete slot route available without a catalogue redirect', async () => {
    const slotRoute: SlotRouteDefinition = {
      id: 'cosmic-fortune', title: 'Cosmic Fortune', shortTitle: 'Cosmic Fortune',
      playPath: '/slots/cosmic-fortune', demoPath: '/slots/cosmic-fortune/demo',
      serverGameIds: ['cosmic-fortune-v1'], shellBackdrop: 'theme', load: vi.fn(),
    }
    await renderRoute('/slots/cosmic-fortune', slotRoute)
    expect(host.textContent).toBe('Slot game')
    expect(state.replace).not.toHaveBeenCalled()
  })
})

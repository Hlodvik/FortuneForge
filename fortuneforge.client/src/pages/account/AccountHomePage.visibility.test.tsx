// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RecentGameActivity } from '../../features/catalog/recentGameActivity'
import type { CataloguePopularityGame } from '../../features/catalog/catalogPopularity'
import { notifyCatalogueChanged } from '../../features/catalog/catalogVisibility'
import { HOME_GAME_CATALOG } from './homeGameCatalog'

const state = vi.hoisted(() => ({
  recent: [] as RecentGameActivity[],
  popularity: [] as CataloguePopularityGame[],
}))

vi.mock('../../components/PlayerHeader', () => ({
  PlayerAccountMenus: ({ showBalance }: { showBalance: boolean }) => <div data-testid="account-menu" data-show-balance={showBalance}>Player menu</div>,
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: { userId: 'visibility-player', playerName: 'Avery', balances: { slotsCredits: 100, freeGames: 0 } }, error: null, isLoading: false, reload: vi.fn(),
}) }))
vi.mock('../../features/catalog/recentGameActivity', () => ({ useRecentGameActivity: () => ({
  games: state.recent, isLoading: false, error: null, refresh: vi.fn(),
}) }))
vi.mock('../../features/catalog/catalogPopularity', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/catalog/catalogPopularity')>(),
  useCataloguePopularity: () => ({ games: state.popularity, isLoading: false, error: '', reload: vi.fn() }),
}))

import { HomePage } from './AccountHomePage'

let version = 0
let host: HTMLDivElement
let root: Root
function visible(...routes: string[]) {
  notifyCatalogueChanged({ version: ++version, games: HOME_GAME_CATALOG.map(game => ({ id: game.id, route: game.href, visible: routes.includes(game.href) })) })
}
const recent = (gameId: string, playedAtUtc = '2026-10-26T12:00:00Z'): RecentGameActivity => ({ gameId, playedAtUtc })

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('BroadcastChannel', undefined)
  vi.stubGlobal('fetch', vi.fn())
  window.history.replaceState(null, '', '/home')
  state.recent = []; state.popularity = []
  visible()
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const render = () => act(async () => { root.render(<HomePage />) })
const highlight = () => host.querySelector<HTMLAnchorElement>('.home-lobby__continue')
const railRoutes = () => [...host.querySelectorAll<HTMLAnchorElement>('.game-catalogue-rail .home-lobby__game-card')].map(link => link.getAttribute('href'))
const gridRoutes = () => [...host.querySelectorAll<HTMLAnchorElement>('.home-lobby__game-grid .home-lobby__game-card')].map(link => link.getAttribute('href'))
async function filter(label: string) {
  const button = [...host.querySelectorAll<HTMLButtonElement>('.home-lobby__filters button')].find(button => button.textContent === label)!
  await act(async () => button.click())
}

describe('home catalogue visibility', () => {
  it('lists available games without inventing a featured or last-played game', async () => {
    visible('/cards/solitaire')
    await render()
    expect(highlight()).toBeNull()
    expect(host.querySelector('.home-lobby__spotlights')).toBeNull()
    expect(railRoutes()).toEqual(['/cards/solitaire'])
    expect(host.textContent).not.toContain('Featured today')
    expect(host.textContent).not.toContain('Last played')
    expect(host.querySelector('input[type="search"]')).toBeNull()
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
  })

  it('omits a hidden recent card game without substituting an unplayed visible game', async () => {
    state.recent = [recent('blackjack')]
    visible('/cards/solitaire')
    await render()
    expect(highlight()).toBeNull()
    expect(railRoutes()).toEqual(['/cards/solitaire'])
    expect(host.textContent).not.toContain('Featured today')
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
  })

  it('does not link a hidden recent slot even when it is newer than card history', async () => {
    state.recent = [recent('blackjack', '2026-10-26T10:00:00Z'), recent('rainbow-realm')]
    visible('/cards/solitaire')
    await render()
    expect(highlight()).toBeNull()
    expect(railRoutes()).toEqual(['/cards/solitaire'])
    expect(host.querySelector('a[href="/slots/rainbow-realm"]')).toBeNull()
  })

  it('shows an empty catalogue without a fabricated highlight when every game is hidden', async () => {
    state.recent = [recent('blackjack')]
    await render()
    expect(highlight()).toBeNull()
    expect(host.textContent).toContain('No games are currently available in this category.')
    expect(host.querySelectorAll('a[href^="/cards/"], a[href^="/slots/"]')).toHaveLength(0)
    expect(host.querySelectorAll('.home-lobby__game-card, .home-lobby__discovery')).toHaveLength(0)
  })

  it('removes a visible recent-game link immediately when the admin hides it', async () => {
    state.recent = [recent('blackjack')]
    visible('/cards/blackjack')
    await render()
    expect(highlight()?.textContent).toContain('Last played')
    expect(railRoutes()).toEqual(['/cards/blackjack'])
    expect(highlight()?.getAttribute('href')).toBe('/cards/blackjack')

    act(() => visible())
    expect(highlight()).toBeNull()
    expect(railRoutes()).toHaveLength(0)
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('uses the next visible recorded play across slots and non-card table games', async () => {
    state.recent = [recent('blackjack', '2026-10-26T10:00:00Z'), recent('dragon-tiger', '2026-10-26T11:00:00Z'), recent('rainbow-realm')]
    visible('/cards/blackjack', '/games/dragon-tiger', '/slots/rainbow-realm')
    await render()
    expect(highlight()?.getAttribute('href')).toBe('/slots/rainbow-realm')
    expect(highlight()?.textContent).toContain('Last played')
    act(() => visible('/cards/blackjack', '/games/dragon-tiger'))
    expect(highlight()?.getAttribute('href')).toBe('/games/dragon-tiger')
    expect(host.querySelector('a[href="/slots/rainbow-realm"]')).toBeNull()
  })

  it('keeps a selected category while live visibility hides and restores its games', async () => {
    visible('/cards/blackjack', '/games/dragon-tiger', '/slots/rainbow-realm', '/games/mines')
    await render()
    await filter('Table games')
    expect(gridRoutes()).toEqual(['/games/dragon-tiger'])
    expect(host.querySelector('.game-catalogue-rail')).toBeNull()
    act(() => visible('/cards/blackjack', '/slots/rainbow-realm', '/games/mines'))
    expect(gridRoutes()).toHaveLength(0)
    expect(host.textContent).toContain('No games are currently available in this category.')
    act(() => visible('/cards/blackjack', '/games/dragon-tiger', '/slots/rainbow-realm', '/games/mines'))
    expect(gridRoutes()).toEqual(['/games/dragon-tiger'])
    await filter('Other games')
    expect(gridRoutes()).toEqual(['/games/mines'])
    await filter('Card games')
    expect(gridRoutes()).toEqual(['/cards/blackjack'])
    await filter('Slots')
    expect(gridRoutes()).toEqual(['/slots/rainbow-realm'])
    expect(host.querySelector('.game-catalogue-rail__arrow')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('orders only visible games by popularity and reapplies order after unhiding', async () => {
    state.popularity = [
      { id: 'blackjack', route: '/cards/blackjack', playCount: 1000 },
      { id: 'solitaire', route: '/cards/solitaire', playCount: 5 },
      { id: 'mines', route: '/games/mines', playCount: 100 },
    ]
    visible('/cards/solitaire', '/games/mines')
    await render()
    expect(railRoutes()).toEqual(['/games/mines', '/cards/solitaire'])
    act(() => visible('/cards/blackjack', '/cards/solitaire', '/games/mines'))
    expect(railRoutes()).toEqual(['/cards/blackjack', '/games/mines', '/cards/solitaire'])
    expect(highlight()).toBeNull()
  })

  it('shows all forty available games once rather than limiting the row to recent play', async () => {
    state.recent = [recent('texas-holdem')]
    visible(...HOME_GAME_CATALOG.map(game => game.href))
    await render()
    expect(railRoutes()).toHaveLength(40)
    expect(new Set(railRoutes()).size).toBe(40)
    expect(highlight()?.getAttribute('href')).toBe('/cards/texas-holdem')
    await filter('Slots')
    expect(gridRoutes()).toHaveLength(20)
    expect(railRoutes()).toHaveLength(0)
    await filter('Popular')
    expect(railRoutes()).toHaveLength(40)
    expect(gridRoutes()).toHaveLength(0)
  })
})

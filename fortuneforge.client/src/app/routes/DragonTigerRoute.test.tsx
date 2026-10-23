// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import routeSource from './DragonTigerRoute.tsx?raw'
import appRoutesSource from '../AppRoutes.tsx?raw'
import pageTitleSource from '../usePageTitle.ts?raw'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import type { DragonTigerCard } from '@fortuneforge/games-dragon-tiger'

type GameProps = { playerId: string; gateway: { requestFn: typeof fetch }; onBalanceChange?: (balance: number) => void; renderCardFace?: (card: DragonTigerCard) => ReactNode }
const state = vi.hoisted(() => ({
  account: null as AccountSummary | null,
  loading: false,
  games: [] as GameProps[],
  fetch: vi.fn<typeof fetch>(),
  refresh: vi.fn<() => Promise<AccountSummary>>(),
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({ account: state.account, error: null, isLoading: state.loading, reload: vi.fn() }),
}))
vi.mock('../../features/account/services/accountsApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/account/services/accountsApi')>(),
  fetchWithAccountSession: state.fetch,
  getCurrentAccount: state.refresh,
}))
vi.mock('@fortuneforge/games-dragon-tiger', () => ({
  HttpDragonTigerGateway: class {
    basePath: string
    requestFn: typeof fetch
    constructor(basePath: string, requestFn: typeof fetch) { this.basePath = basePath; this.requestFn = requestFn }
  },
  DragonTigerGame: (props: GameProps) => { state.games.push(props); return <div data-testid="dragon-tiger-game" data-player-id={props.playerId} /> },
}))
import { AuthenticatedDragonTigerRoute } from './DragonTigerRoute'

const account = (userId: string, credits: number): AccountSummary => ({
  userId, playerName: userId, email: `${userId}@example.test`, createdAtUtc: '2026-10-23T00:00:00Z', role: 'player',
  balances: { slotsCredits: credits, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
})
let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState(null, '', '/games/dragon-tiger')
  state.account = account('first', 100)
  state.loading = false
  state.games = []
  state.fetch.mockReset().mockResolvedValue(new Response('{}'))
  state.refresh.mockReset()
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const render = () => act(() => root.render(<AuthenticatedDragonTigerRoute />))
const game = () => state.games.at(-1)!

describe('Dragon Tiger connected route', () => {
  it('renders actual shared court/number faces while leaving game-specific backs to the package', () => {
    render()
    const renderer = game().renderCardFace!
    const queen = renderToStaticMarkup(renderer({ rank: 'queen', suit: 'hearts' }))
    expect(queen).toContain('ff-playing-card--face ff-playing-card--red')
    expect(queen).toContain('ff-playing-card__center--court')
    expect(queen).toContain('Queen of hearts')
    const ten = renderToStaticMarkup(renderer({ rank: 'ten', suit: 'clubs' }))
    expect(ten).toContain('ff-playing-card--face ff-playing-card--black')
    expect(ten).toContain('Ten of clubs')
    expect(ten).not.toContain('ff-playing-card--back')
    render()
    expect(game().renderCardFace).toBe(renderer)
  })

  it('uses the existing shared navbar with real account exits and a route-scoped black theme', () => {
    render()
    expect(host.querySelector('.dragon-tiger-page [data-game-navbar]')).not.toBeNull()
    expect(host.querySelector('a[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/home')
    expect(host.querySelector('a.player-shell-header__games')?.getAttribute('href')).toBe('/games')
    expect(host.querySelector('.in-game-navbar__title')?.textContent).toBe('Dragon Tiger')
  })

  it('keeps the table behind authentication and registers the lazy route and title', () => {
    state.account = null
    state.loading = true
    render()
    expect(state.games).toHaveLength(0)
    expect(host.textContent).toContain('Opening Dragon Tiger')
    expect(routeSource).toContain("useAuthenticatedAccount('/games/dragon-tiger')")
    expect(appRoutesSource).toContain("pathname === '/games/dragon-tiger'")
    expect(pageTitleSource).toContain("'/games/dragon-tiger': 'Dragon Tiger — Fortune Forge'")
  })

  it('uses authenticated requests, account-scoped recovery, and refreshes the real shared wallet', async () => {
    render()
    expect(game().playerId).toBe('first:account')
    expect(game().gateway.requestFn).toBe(state.fetch)
    expect(game().onBalanceChange).toBeTypeOf('function')
    state.refresh.mockResolvedValue(account('first', 112.5))

    await act(async () => { game().onBalanceChange!(112.5); await Promise.resolve() })

    expect(state.refresh).toHaveBeenCalledOnce()
    expect(host.querySelector('.player-shell-menu--balance')?.textContent).toContain('112.5')
  })

  it('adds only the practice header and never replaces the real navbar wallet with practice funds', async () => {
    window.history.replaceState(null, '', '/games/dragon-tiger?mode=practice')
    render()
    expect(game().playerId).toBe('first:practice')
    expect(game().onBalanceChange).toBeUndefined()
    await game().gateway.requestFn('/api/games/dragon-tiger/status', { headers: { 'Idempotency-Key': 'original_request_key' } })

    const headers = new Headers(state.fetch.mock.calls[0]![1]?.headers)
    expect(headers.get('X-FortuneForge-Practice')).toBe('true')
    expect(headers.get('Idempotency-Key')).toBe('original_request_key')
    expect(state.refresh).not.toHaveBeenCalled()
    expect(host.querySelector('.in-game-navbar__actions')?.textContent).toBe('Practice active')
  })

  it('does not apply a late old-account wallet refresh to a newly signed-in account', async () => {
    let finish!: (value: AccountSummary) => void
    state.refresh.mockReturnValue(new Promise(resolve => { finish = resolve }))
    render()
    act(() => game().onBalanceChange!(12))
    state.account = account('second', 700)
    render()

    await act(async () => { finish(account('first', 12)); await Promise.resolve() })

    expect(game().playerId).toBe('second:account')
    expect(host.querySelector('.player-shell-menu--balance')?.textContent).toContain('700')
    expect(host.querySelector('.player-shell-menu--balance')?.textContent).not.toContain('12.00')
  })
})

// @vitest-environment jsdom
import { act, type ComponentProps, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import routeSource from './AndarBaharRoute.tsx?raw'
import appRoutesSource from '../AppRoutes.tsx?raw'
import pageTitleSource from '../usePageTitle.ts?raw'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import type { AndarBaharCard } from '@fortuneforge/games-andar-bahar'
import { PlayingCard } from '../../games/cards/shared/PlayingCard'

type GameProps = { playerId: string; gateway: { basePath: string; requestFn: typeof fetch }; onBalanceChange?: (balance: number) => void; renderCardFace?: (card: AndarBaharCard) => ReactNode }
const state = vi.hoisted(() => ({
  account: null as AccountSummary | null, loading: false, error: null as string | null, games: [] as GameProps[],
  shellAccounts: [] as (AccountSummary | undefined)[],
  fetch: vi.fn<typeof fetch>(), refresh: vi.fn<() => Promise<AccountSummary>>(), reload: vi.fn(),
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({ account: state.account, error: state.error, isLoading: state.loading, reload: state.reload }),
}))
vi.mock('../../features/account/services/accountsApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/account/services/accountsApi')>(),
  fetchWithAccountSession: state.fetch, getCurrentAccount: state.refresh,
}))
vi.mock('../../components/InGameShell', async importOriginal => {
  const original = await importOriginal<typeof import('../../components/InGameShell')>()
  return {
    ...original,
    InGameShell: (props: ComponentProps<typeof original.InGameShell>) => {
      state.shellAccounts.push(props.account)
      return <original.InGameShell {...props} />
    },
  }
})
vi.mock('@fortuneforge/games-andar-bahar', () => ({
  HttpAndarBaharGateway: class {
    basePath: string
    requestFn: typeof fetch
    constructor(basePath: string, requestFn: typeof fetch) { this.basePath = basePath; this.requestFn = requestFn }
  },
  AndarBaharGame: (props: GameProps) => { state.games.push(props); return <div data-testid="andar-bahar-game" data-player-id={props.playerId} /> },
}))
import { AuthenticatedAndarBaharRoute } from './AndarBaharRoute'

const account = (userId: string, credits: number): AccountSummary => ({
  userId, playerName: userId, email: `${userId}@example.test`, createdAtUtc: '2026-10-24T00:00:00Z', role: 'player',
  balances: { slotsCredits: credits, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
})
let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState(null, '', '/games/andar-bahar')
  state.account = account('first', 100); state.loading = false; state.error = null; state.games = []
  state.shellAccounts = []
  state.fetch.mockReset().mockResolvedValue(new Response('{}')); state.refresh.mockReset(); state.reload.mockReset()
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const render = () => act(() => root.render(<AuthenticatedAndarBaharRoute />))
const game = () => state.games.at(-1)!
const shellAccount = () => state.shellAccounts.at(-1)!

describe('Andar Bahar connected route', () => {
  it('uses the exact canonical shared card faces without an alternate deck or changing table-specific backs', () => {
    render()
    const renderer = game().renderCardFace!
    expect(renderer).toBeTypeOf('function')
    const king = renderToStaticMarkup(renderer({ rank: 'king', suit: 'hearts' }))
    expect(king).toBe(renderToStaticMarkup(<PlayingCard card={{ id: 'reference-king', rank: 13, suit: 'hearts' }} />))
    expect(king).toContain('ff-playing-card--face ff-playing-card--red')
    expect(king).toContain('ff-playing-card__center--court')
    expect(king).toContain('aria-label="King of hearts"')
    expect(king).toContain('class="ff-playing-card__court-art"')
    expect(king).toContain('data-court-symbol="king"')
    expect(king).not.toContain('<img')
    const ten = renderToStaticMarkup(renderer({ rank: 'ten', suit: 'clubs' }))
    expect(ten).toBe(renderToStaticMarkup(<PlayingCard card={{ id: 'reference-ten', rank: 10, suit: 'clubs' }} />))
    expect(ten).toContain('ff-playing-card--face ff-playing-card--black')
    expect(ten).toContain('aria-label="Ten of clubs"')
    expect(ten).not.toContain('ff-playing-card__center--court')
    expect(ten).not.toContain('ff-playing-card--back')
    expect(routeSource).toContain("import '../../games/cards/shared/playingCards.css'")
    expect(routeSource).not.toContain('AssetPlayingCard')
    render()
    expect(game().renderCardFace).toBe(renderer)
  })

  it('keeps authentication in front of the game and registers its lazy route and title', () => {
    state.account = null; state.loading = true; render()
    expect(state.games).toHaveLength(0); expect(host.textContent).toContain('Opening Andar Bahar')
    expect(routeSource).toContain("useAuthenticatedAccount('/games/andar-bahar')")
    expect(appRoutesSource).toContain("pathname === '/games/andar-bahar'")
    expect(pageTitleSource).toContain("'/games/andar-bahar': 'Andar Bahar — Fortune Forge'")
  })

  it('offers retry on authentication failure without instantiating a game', () => {
    state.account = null; state.error = 'The account is unavailable.'; render()
    expect(state.games).toHaveLength(0)
    expect(host.textContent).toContain('Andar Bahar could not be opened.')
    act(() => host.querySelector('button')!.click())
    expect(state.reload).toHaveBeenCalledOnce()
  })

  it('uses shared navigation with real account exits', () => {
    render()
    expect(host.querySelector('.andar-bahar-page [data-game-navbar]')).not.toBeNull()
    expect(host.querySelector('a[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/home')
    expect(host.querySelector('a.player-shell-header__games')?.getAttribute('href')).toBe('/games')
    expect(host.querySelector('.in-game-navbar__title')).toBeNull()
    expect(host.querySelector('.in-game-navbar__actions')).toBeNull()
    expect(host.textContent).not.toContain('Try practice')
    expect(host.querySelector('.player-shell-menu--balance')).toBeNull()
    expect(host.querySelector('.player-shell-menu--more nav[aria-label="Account menu"]')).not.toBeNull()
  })

  it('uses account-session requests and refreshes the real wallet only after paid settlement', async () => {
    render()
    expect(game().playerId).toBe('first:account')
    expect(game().gateway.basePath).toBe('/api/games/andar-bahar')
    expect(game().gateway.requestFn).toBe(state.fetch)
    state.refresh.mockResolvedValue(account('first', 100.45))
    await act(async () => { game().onBalanceChange!(100.45); await Promise.resolve() })
    expect(state.refresh).toHaveBeenCalledOnce()
    expect(shellAccount().balances.slotsCredits).toBe(100.45)
  })

  it('isolates practice requests and recovery without replacing the real account state', async () => {
    window.history.replaceState(null, '', '/games/andar-bahar?mode=practice'); render()
    expect(game().playerId).toBe('first:practice'); expect(game().onBalanceChange).toBeUndefined()
    await game().gateway.requestFn('/api/games/andar-bahar/rounds', { headers: { 'X-FortuneForge-Game': 'andar-bahar' } })
    const headers = new Headers(state.fetch.mock.calls[0]![1]?.headers)
    expect(headers.get('X-FortuneForge-Practice')).toBe('true')
    expect(headers.get('X-FortuneForge-Game')).toBe('andar-bahar')
    expect(state.refresh).not.toHaveBeenCalled()
    expect(host.querySelector('.in-game-navbar__title')).toBeNull()
    expect(host.querySelector('.in-game-navbar__actions')).toBeNull()
    expect(host.textContent).not.toContain('Practice active')
    expect(host.textContent).not.toContain('Try practice')
    expect(host.querySelector('.player-shell-menu--balance')).toBeNull()
    expect(host.querySelector('.player-shell-menu--more nav[aria-label="Account menu"]')).not.toBeNull()
    expect(shellAccount().balances.slotsCredits).toBe(100)
  })

  it('ignores a delayed old-account wallet response after account change', async () => {
    let finish!: (value: AccountSummary) => void
    state.refresh.mockReturnValue(new Promise(resolve => { finish = resolve }))
    render(); act(() => game().onBalanceChange!(12))
    state.account = account('second', 700); render()
    await act(async () => { finish(account('first', 12)); await Promise.resolve() })
    expect(game().playerId).toBe('second:account')
    expect(shellAccount().userId).toBe('second')
    expect(shellAccount().balances.slotsCredits).toBe(700)
  })

  it('ignores a wallet refresh returning a different account', async () => {
    state.refresh.mockResolvedValue(account('other', 900))
    render()
    await act(async () => { game().onBalanceChange!(900); await Promise.resolve() })
    expect(game().playerId).toBe('first:account')
    expect(shellAccount().userId).toBe('first')
    expect(shellAccount().balances.slotsCredits).toBe(100)
  })

  it('does not let an older same-account wallet refresh overwrite a later settlement', async () => {
    let first!: (value: AccountSummary) => void
    let second!: (value: AccountSummary) => void
    state.refresh
      .mockImplementationOnce(() => new Promise(resolve => { first = resolve }))
      .mockImplementationOnce(() => new Promise(resolve => { second = resolve }))
    render()
    act(() => { game().onBalanceChange!(100.45); game().onBalanceChange!(101.40) })
    await act(async () => { second(account('first', 101.40)); await Promise.resolve() })
    expect(shellAccount().balances.slotsCredits).toBe(101.4)
    await act(async () => { first(account('first', 100.45)); await Promise.resolve() })
    expect(shellAccount().balances.slotsCredits).toBe(101.4)
  })
})

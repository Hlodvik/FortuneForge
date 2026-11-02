// @vitest-environment jsdom
import { act, type ComponentProps } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { MinesGameProps, MinesGateway, MinesSession } from '@fortuneforge/games-mines'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import routeSource from './MinesRoute.tsx?raw'
import appRoutesSource from '../AppRoutes.tsx?raw'
import { pageTitleForPath } from '../usePageTitle'

const state = vi.hoisted(() => ({
  account: null as AccountSummary | null,
  loading: false,
  error: null as string | null,
  games: [] as MinesGameProps[],
  shellAccounts: [] as (AccountSummary | undefined)[],
  authenticate: vi.fn(), reload: vi.fn(), fetch: vi.fn<typeof fetch>(), refresh: vi.fn(),
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: (path: string) => {
    state.authenticate(path)
    return { account: state.account, error: state.error, isLoading: state.loading, reload: state.reload }
  },
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
vi.mock('@fortuneforge/games-mines', async importOriginal => ({
  ...await importOriginal<typeof import('@fortuneforge/games-mines')>(),
  MinesGame: (props: MinesGameProps) => {
    state.games.push(props)
    return <div className="ff-mines" data-testid="mines-game" data-player-id={props.playerId} />
  },
}))
import { AuthenticatedMinesRoute } from './MinesRoute'

const account = (userId: string, credits: number): AccountSummary => ({
  userId, playerName: userId, email: `${userId}@example.test`, createdAtUtc: '2026-10-25T00:00:00Z', role: 'player',
  balances: { slotsCredits: credits, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
})
const session: MinesSession = {
  balanceCents: 123456, practice: false, round: null,
  minimumWagerCents: 100, maximumWagerCents: 500, houseEdgeBasisPoints: 1000, payoutPolicyVersion: 2,
}
let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState(null, '', '/games/mines')
  state.account = account('first', 1234.56)
  state.loading = false; state.error = null; state.games = []; state.shellAccounts = []
  state.authenticate.mockReset(); state.reload.mockReset(); state.refresh.mockReset()
  state.fetch.mockReset().mockImplementation(async () => new Response(JSON.stringify(session)))
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const render = () => act(() => root.render(<AuthenticatedMinesRoute />))
const game = () => state.games.at(-1)!
const gateway = () => game().gateway as MinesGateway

describe('Mines production host route', () => {
  it('waits for authenticated account resolution and registers a lazy route and title', () => {
    state.account = null; state.loading = true; render()
    expect(state.authenticate).toHaveBeenCalledWith('/games/mines')
    expect(state.games).toHaveLength(0)
    expect(host.textContent).toContain('Opening Mines')
    expect(appRoutesSource).toContain("import('./routes/MinesRoute')")
    expect(appRoutesSource).toContain("pathname === '/games/mines'")
    expect(pageTitleForPath('/games/mines')).toBe('Mines — Fortune Forge')
  })

  it('offers an account retry without creating a playable session on auth failure', () => {
    state.account = null; state.error = 'Please sign in again.'; render()
    expect(state.games).toHaveLength(0)
    expect(host.textContent).toContain('Mines could not be opened.')
    act(() => host.querySelector('button')!.click())
    expect(state.reload).toHaveBeenCalledOnce()
    expect(state.fetch).not.toHaveBeenCalled()
  })

  it('uses the real shared navbar, account exits and reporting, without a duplicate wallet', () => {
    render()
    expect(host.querySelector('.in-game-shell > [data-game-navbar]')).not.toBeNull()
    expect(host.querySelector('.in-game-shell > [data-game-surface].mines-page')).not.toBeNull()
    expect(host.querySelector('[data-game-navbar]')?.closest('[data-game-surface]')).toBeNull()
    expect(host.querySelector('a[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/home')
    expect(host.querySelector('a.player-shell-header__games')?.getAttribute('href')).toBe('/home#home-lobby-games-title')
    expect(host.querySelector('button[aria-label="Report this game"]')).not.toBeNull()
    expect(host.querySelector('.player-shell-menu--balance')).toBeNull()
    const navbar = host.querySelector('[data-game-navbar]')!
    expect(host.querySelectorAll('[data-site-navbar]')).toHaveLength(1)
    expect(host.querySelectorAll('.site-header-balance')).toHaveLength(1)
    const balance = navbar.querySelector('.site-header-balance')!
    expect(balance.getAttribute('href')).toBe('/balance')
    expect(balance.getAttribute('aria-label')).toBe('Balance R1,234.56. Add funds')
    expect(balance.querySelector('.site-header-balance__full')?.textContent).toBe('R1,234.56')
    expect(balance.querySelector('.site-header-balance__whole[aria-hidden="true"]')?.textContent).toBe('R1,234')
    expect(host.querySelector('.player-shell-menu--more nav[aria-label="Account menu"]')).not.toBeNull()
  })

  it('offers no practice mode or mode toggle', () => {
    render()
    expect(host.querySelector('.in-game-navbar__actions')).toBeNull()
    expect(host.textContent).not.toContain('Practice only')
    expect(host.textContent).not.toContain('Return to account play')
    expect(host.textContent).not.toContain('Try practice')
    expect(game().currencySymbol).toBe('R')
    expect(game().onBalanceChange).toBeTypeOf('function')
    expect(routeSource).not.toContain('walletPracticeModeEnabled')
    expect(routeSource).not.toContain('practiceAccountFetch')
  })

  it.each(['/games/mines', '/games/mines?mode=practice', '/games/mines?mode=account'])
  ('uses authenticated account-credit requests regardless of URL mode at %s', async path => {
    window.history.replaceState(null, '', path); render()
    expect(game().playerId).toBe('first:account')
    expect(await gateway().getSession()).toEqual(session)
    expect(await gateway().execute({ action: 'start', requestId: 'test-start', wagerCents: 100, mineCount: 3 })).toEqual(session)
    expect(state.fetch.mock.calls.map(([url]) => url)).toEqual(['/api/games/mines/session', '/api/games/mines/rounds'])
    for (const [, init] of state.fetch.mock.calls) {
      expect(new Headers(init?.headers).has('X-FortuneForge-Practice')).toBe(false)
      expect(init?.credentials).toBe('same-origin')
    }
    expect(state.fetch.mock.calls[1]![1]?.body).toBe(JSON.stringify({ requestId: 'test-start', wagerCents: 100, mineCount: 3 }))
    expect(state.refresh).not.toHaveBeenCalled()
    expect(state.shellAccounts.at(-1)?.balances.slotsCredits).toBe(1234.56)
  })

  it('preserves one gateway and account-mode storage scope through same-account rerenders', () => {
    render(); const firstGateway = gateway()
    state.account = account('first', 1200); render()
    expect(gateway()).toBe(firstGateway)
    expect(game().playerId).toBe('first:account')
    expect(state.shellAccounts.at(-1)?.balances.slotsCredits).toBe(1200)
  })

  it('remounts the gateway and recovery scope when the signed-in account changes', () => {
    render(); const firstGateway = gateway()
    state.account = account('second', 700); render()
    expect(gateway()).not.toBe(firstGateway)
    expect(game().playerId).toBe('second:account')
    expect(state.shellAccounts.at(-1)?.userId).toBe('second')
    expect(game().onBalanceChange).toBeTypeOf('function')
  })

  it('refreshes the authoritative currency-unit account summary instead of writing callback cents into it', async () => {
    state.refresh.mockResolvedValue(account('first', 1235.58))
    render()
    await act(async () => { game().onBalanceChange!(123558); await Promise.resolve() })
    expect(state.refresh).toHaveBeenCalledOnce()
    expect(state.shellAccounts.at(-1)?.balances.slotsCredits).toBe(1235.58)
  })

  it('ignores a delayed old-account balance refresh after account replacement', async () => {
    let finish!: (value: AccountSummary) => void
    state.refresh.mockReturnValue(new Promise(resolve => { finish = resolve }))
    render(); act(() => game().onBalanceChange!(20000))
    state.account = account('second', 700); render()
    await act(async () => { finish(account('first', 200)); await Promise.resolve() })
    expect(game().playerId).toBe('second:account')
    expect(state.shellAccounts.at(-1)?.userId).toBe('second')
    expect(state.shellAccounts.at(-1)?.balances.slotsCredits).toBe(700)
  })

  it('does not let an older refresh overwrite a newer settlement or accept another account response', async () => {
    let first!: (value: AccountSummary) => void
    let second!: (value: AccountSummary) => void
    state.refresh
      .mockImplementationOnce(() => new Promise(resolve => { first = resolve }))
      .mockImplementationOnce(() => new Promise(resolve => { second = resolve }))
      .mockResolvedValue(account('other', 900))
    render()
    act(() => { game().onBalanceChange!(123400); game().onBalanceChange!(123558) })
    await act(async () => { second(account('first', 1235.58)); await Promise.resolve() })
    await act(async () => { first(account('first', 1234)); await Promise.resolve() })
    expect(state.shellAccounts.at(-1)?.balances.slotsCredits).toBe(1235.58)
    await act(async () => { game().onBalanceChange!(90000); await Promise.resolve() })
    expect(state.shellAccounts.at(-1)?.userId).toBe('first')
    expect(state.shellAccounts.at(-1)?.balances.slotsCredits).toBe(1235.58)
  })

  it('propagates a disabled-paid service response without retrying on a practice ledger', async () => {
    state.fetch.mockResolvedValue(new Response(JSON.stringify({ code: 'mines-paid-disabled', message: 'Account play is not available yet.' }), { status: 503 }))
    render()
    await expect(gateway().getSession()).rejects.toMatchObject({ status: 503, code: 'mines-paid-disabled' })
    expect(state.fetch).toHaveBeenCalledOnce()
    expect(new Headers(state.fetch.mock.calls[0]![1]?.headers).has('X-FortuneForge-Practice')).toBe(false)
  })
})

// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'

const state = vi.hoisted(() => ({
  recent: [] as { gameId: string; playedAtUtc: string }[], recentError: null as string | null,
  popularity: null as { id: string; route: string; playCount: number | null }[] | null,
  popularityError: '', hiddenRoutes: [] as string[], balance: 139174.75,
  loading: false, error: null as string | null, signedOut: false, playerName: 'Avery', returnPath: '', reload: vi.fn(),
}))
afterEach(() => {
  state.recent = []; state.recentError = null; state.popularity = null; state.popularityError = ''; state.hiddenRoutes = []
  state.balance = 139174.75; state.loading = false; state.error = null; state.signedOut = false; state.playerName = 'Avery'; state.reload.mockReset()
  window.history.replaceState(null, '', '/')
  vi.unstubAllGlobals()
})
vi.mock('../../components/PlayerHeader', () => ({ PlayerAccountMenus: ({ showBalance }: { showBalance: boolean }) =>
  <details data-show-balance={showBalance}><summary>Account menu</summary><button>Log out</button></details>,
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: (returnPath: string) => {
  state.returnPath = returnPath
  return { account: state.signedOut ? null : { userId: 'home-test', playerName: state.playerName, role: 'player', balances: { slotsCredits: state.balance } },
    error: state.error, isLoading: state.loading, reload: state.reload }
} }))
vi.mock('../../features/catalog/recentGameActivity', () => ({ useRecentGameActivity: () => ({ games: state.recent, isLoading: false, error: state.recentError, refresh: vi.fn() }) }))
vi.mock('../../features/catalog/catalogPopularity', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/catalog/catalogPopularity')>(),
  useCataloguePopularity: () => ({ games: state.popularity, isLoading: false, error: state.popularityError, reload: vi.fn() }),
}))
vi.mock('../../features/catalog/catalogVisibility', () => ({ useCatalogueVisibility: () => ({ loading: false, error: '', isVisible: (route: string) => !state.hiddenRoutes.includes(route) }) }))

import { HomePage } from './AccountHomePage'
const staticHome = () => { const host = document.createElement('div'); host.innerHTML = renderToStaticMarkup(<HomePage />); return host }
const play = (gameId: string, hour = 12) => ({ gameId, playedAtUtc: '2026-10-26T' + hour + ':00:00Z' })

describe('account home', () => {
  it('keeps the spade and personal greeting, with balance only at the top right', () => {
    const host = staticHome()
    expect(host.querySelector('.home-lobby__brand-spade')?.getAttribute('fill')).toBe('currentColor')
    expect(host.querySelector('h1')?.textContent).toBe('Welcome back, Avery')
    expect(host.querySelector('.home-lobby__header-balance')?.getAttribute('href')).toBe('/home/rand')
    expect(host.querySelector('.home-lobby__topbar')?.textContent).toContain('R139,174.75')
    expect(host.innerHTML.match(/R139,174\.75/g)).toHaveLength(1)
    expect(host.innerHTML).toContain('data-show-balance="false"')
    expect(host.querySelector('.home-lobby__balance, input[type="search"], .home-lobby__browse, .home-lobby__top-help')).toBeNull()
    expect(host.querySelectorAll('button[aria-label="Help"]')).toHaveLength(1)
    expect(host.querySelector('.home-lobby__sidebar button[aria-label="Help"]')).not.toBeNull()
    expect(host.querySelector('.home-lobby__navigation a[href="/home/history"]')).not.toBeNull()
    expect(host.querySelector('.home-lobby__main a[href="/home/history"]')).toBeNull()
  })
  it('shows all 40 games immediately, even with recent activity', () => {
    state.recent = [play('blackjack')]
    const host = staticHome()
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(40)
    expect(new Set(Array.from(host.querySelectorAll('.home-lobby__game-card')).map(card => card.getAttribute('href'))).size).toBe(40)
    expect(host.querySelector('.home-lobby__game-card > span')).toBeNull()
    expect(Array.from(host.querySelectorAll('.home-lobby__filters button')).map(button => button.textContent)).toEqual(['Popular', 'Slots', 'Card games', 'Table games', 'Other games'])
    expect(host.querySelector('.game-catalogue-rail')).not.toBeNull()
    expect(host.querySelector('.home-lobby__game-grid')).toBeNull()
  })
  it('uses the full Texas Hold’em name without session subtext', () => {
    state.recent = [play('texas-holdem')]
    const highlight = staticHome().querySelector('.home-lobby__continue')!
    expect(highlight.getAttribute('href')).toBe('/cards/texas-holdem')
    expect(highlight.querySelector('h2')?.textContent).toBe('Texas Hold’em')
    expect(highlight.querySelector('p')).toBeNull()
    expect(highlight.textContent).toContain('Last played')
    expect(highlight.textContent).not.toContain('hands')
  })
  it.each(['keno', 'baccarat', '2048', 'drop-merge', 'hearts', 'mines', 'andar-bahar', 'craps'])('uses the newest %s play instead of an older poker session', gameId => {
    state.recent = [play('texas-holdem', 10), play(gameId, 13)]
    const highlight = staticHome().querySelector('.home-lobby__continue')!
    expect(highlight.getAttribute('href')).toBe((gameId === 'hearts' ? '/cards/' : '/games/') + gameId)
    expect(highlight.querySelector('img')?.getAttribute('src')).toBe(getCataloguePresentationArt(highlight.getAttribute('href')!)!.src)
  })
  it('uses canonical slot ids and full names for recent play', () => {
    state.recent = [play('wukong-journey-to-the-west')]
    const highlight = staticHome().querySelector('.home-lobby__continue')!
    expect(highlight.querySelector('h2')?.textContent).toBe("Wukong's Journey to the West")
    expect(highlight.getAttribute('href')).toBe('/slots/wukong')
  })
  it('does not invent recent play for a new account', () => {
    const host = staticHome()
    expect(host.querySelector('.home-lobby__spotlights')).toBeNull()
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(40)
  })
  it('orders by global counts and excludes hidden games', () => {
    state.popularity = [
      { id: 'blackjack', route: '/cards/blackjack', playCount: 500 },
      { id: 'mines', route: '/games/mines', playCount: 300 },
      { id: 'baccarat', route: '/games/baccarat', playCount: 400 },
    ]
    state.hiddenRoutes = ['/cards/blackjack']
    state.recent = [play('blackjack', 13), play('texas-holdem', 12)]
    const host = staticHome()
    const routes = Array.from(host.querySelectorAll('.home-lobby__game-card')).map(card => card.getAttribute('href'))
    expect(routes).toHaveLength(39)
    expect(routes.slice(0, 2)).toEqual(['/games/baccarat', '/games/mines'])
    expect(host.querySelector('.home-lobby__continue')?.getAttribute('href')).toBe('/cards/texas-holdem')
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
  })
  it.each([['Slots', 'Slots', 20], ['Cards', 'Card games', 9], ['Casino', 'Table games', 7], ['Puzzles', 'Other games', 4], ['All%20games', 'Popular', 40], ['Popular', 'Popular', 40], ['invalid', 'Popular', 40]])('preserves %s links across login', (query, label, count) => {
    window.history.replaceState(null, '', '/home?category=' + query + '#home-lobby-games-title')
    const host = staticHome()
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(count)
    expect(host.querySelector('.home-lobby__filters [aria-pressed="true"]')?.textContent).toBe(label)
    expect(state.returnPath).toBe('/home?category=' + query + '#home-lobby-games-title')
    expect(host.querySelector('.game-catalogue-rail') !== null).toBe(label === 'Popular')
    expect(host.querySelector('.home-lobby__game-grid') !== null).toBe(label !== 'Popular')
    if (label !== 'Popular') expect(host.querySelector('.game-catalogue-rail__arrow')).toBeNull()
  })
  it('puts Keno, Drop Merge and 2048 in Other games and restores all games', async () => {
    const host = document.createElement('div'); document.body.append(host)
    const root = createRoot(host); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
    try {
      await act(async () => root.render(<HomePage />))
      await act(async () => Array.from(host.querySelectorAll<HTMLButtonElement>('.home-lobby__filters button')).find(button => button.textContent === 'Other games')!.click())
      expect(Array.from(host.querySelectorAll('.home-lobby__game-card strong')).map(card => card.textContent).sort()).toEqual(['2048', 'Drop Merge', 'Keno', 'Mines'])
      expect(host.querySelector('.home-lobby__game-grid')?.getAttribute('aria-label')).toBe('Other games')
      expect(host.querySelector('.game-catalogue-rail, .game-catalogue-rail__arrow')).toBeNull()
      await act(async () => host.querySelector<HTMLButtonElement>('.home-lobby__filters button')!.click())
      expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(40)
      expect(host.querySelector('.game-catalogue-rail')).not.toBeNull()
      expect(host.querySelector('.home-lobby__game-grid')).toBeNull()
    } finally { await act(async () => root.unmount()); host.remove() }
  })
  it('keeps loading and error states truthful with retry', () => {
    state.signedOut = true; state.loading = true
    expect(staticHome().textContent).toContain('Opening your account')
    expect(staticHome().querySelector('.home-lobby__header-balance')).toBeNull()
    state.loading = false; state.error = 'Your session needs attention.'
    expect(staticHome().textContent).toContain('Your session needs attention.')
    state.signedOut = false; state.error = null; state.recentError = 'Unavailable'; state.popularityError = 'Unavailable'
    const host = staticHome()
    expect(host.textContent).toContain('Your recent play couldn’t be loaded.')
    expect(host.textContent).toContain('Popular order is unavailable.')
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(40)
  })
})

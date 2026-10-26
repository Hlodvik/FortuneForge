// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { CardRoomActivity } from '../../games/cards/shared/cardRoomHistoryTypes'
import type { SlotGameCatalogEntry } from '../../games/slots/catalogTypes'
import { SLOT_GAME_CATALOG } from '../../games/slots/catalog'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'

type LobbyHistoryActivity = CardRoomActivity | (Omit<CardRoomActivity, 'game'> & { game: 'mines' })
const state = vi.hoisted(() => ({
  activities: [] as LobbyHistoryActivity[], slotGame: null as SlotGameCatalogEntry | null,
  slotGames: [] as { game: SlotGameCatalogEntry; playedAtUtc: string }[],
  hiddenRoutes: [] as string[], balance: 100, loading: false, error: null as string | null,
  signedOut: false, reload: vi.fn(),
}))
afterEach(() => {
  state.activities = []; state.slotGame = null; state.slotGames = []; state.hiddenRoutes = []
  state.balance = 100; state.loading = false; state.error = null; state.signedOut = false; state.reload.mockReset()
  vi.unstubAllGlobals()
})

vi.mock('../../components/PlayerHeader', () => ({
  PlayerAccountMenus: ({ showBalance }: { showBalance: boolean }) => <details data-testid="account-menu" data-show-balance={showBalance}><summary>Account menu</summary><button>Log out</button></details>,
}))

vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({
    account: state.signedOut ? null : {
      userId: 'welcome-test',
      playerName: 'Avery',
      email: 'avery@example.test',
      createdAtUtc: '2026-09-12T00:00:00Z',
      balances: { slotsCredits: state.balance, freeGames: 0 },
      slots: {
        spinsPlayed: 0,
        wins: 0,
        losses: 0,
        creditsWagered: 0,
        creditsWon: 0,
        netCredits: 0,
      },
      role: 'player',
    },
    error: state.error,
    isLoading: state.loading,
    reload: state.reload,
  }),
}))

vi.mock('../../games/slots/useRecentSlotGame', () => ({
  useRecentSlotGame: () => ({ game: state.slotGame, games: state.slotGames, playedAtUtc: state.slotGame ? '2026-10-25T13:00:00Z' : null }),
}))

vi.mock('../../features/catalog/catalogVisibility', () => ({
  useCatalogueVisibility: () => ({ loading: false, error: '', isVisible: (route: string) => !state.hiddenRoutes.includes(route) }),
}))

vi.mock('../cards/useCardRoomHistory', () => ({
  useCardRoomHistory: () => ({ activities: state.activities }),
}))

import { HomePage } from './AccountHomePage'

describe('account home', () => {
  it('links a recent Mines settlement to Mines rather than a card table or missing mapping', () => {
    state.activities = [{
      id: 'mines-1', matchId: 'mines-round-1', game: 'mines', gameLabel: 'Mines',
      title: 'Mines game', summary: 'R1.02 won', startedAtUtc: '2026-10-25T12:00:00Z',
      completedAtUtc: '2026-10-25T12:01:00Z', unseen: false, requiresClaim: false, winningsCredits: 1.02,
    }]
    const markup = renderToStaticMarkup(createElement(HomePage))
    expect(markup).toContain('href="/games/mines"')
    expect(markup).toContain('Mines game')
    expect(markup).toContain(getCataloguePresentationArt('/games/mines')!.src)
    expect(markup).toContain('Last played')
    expect(markup).not.toContain('Featured today')
  })
  it('links a recent Andar Bahar settlement to its actual integrated route', () => {
    state.activities = [{
      id: 'andar-bahar-1', matchId: 'round-1', game: 'andar-bahar', gameLabel: 'Andar Bahar',
      title: 'Andar Bahar game', summary: 'R0.95 won', startedAtUtc: '2026-10-24T12:00:00Z',
      completedAtUtc: '2026-10-24T12:00:00Z', unseen: false, requiresClaim: false, winningsCredits: .95,
    }]
    const markup = renderToStaticMarkup(createElement(HomePage))
    expect(markup).toContain('href="/games/andar-bahar"')
    expect(markup).toContain('Andar Bahar')
    expect(markup).not.toContain('Featured today')
  })
  it('welcomes the real player without inventing recent play', () => {
    const markup = renderToStaticMarkup(createElement(HomePage))

    expect(markup).toContain('Welcome back')
    expect(markup).toContain('Avery')
    expect(markup).toContain('home-lobby__dashboard')
    expect(markup).toContain('Featured today')
    expect(markup).toContain('Find your next favourite')
    expect(markup).not.toContain('Recently played')
    expect(markup).not.toContain('Last played')
  })

  it('shows the real balance only in the body and keeps account/funding/history actions', () => {
    state.balance = 139174.75
    const markup = renderToStaticMarkup(createElement(HomePage))
    expect(markup).toContain('R139,174.75')
    expect(markup.match(/R139,174\.75/g)).toHaveLength(1)
    expect(markup).toContain('data-show-balance="false"')
    expect(markup).toContain('href="/home/rand"')
    expect(markup).toContain('href="/home/history"')
    expect(markup).toContain('href="/home/settings"')
    expect(markup).toContain('Log out')
    expect(markup).toContain('aria-current="page"')
  })

  it('combines real history chronologically and deduplicates games', () => {
    state.activities = [
      recentActivity('blackjack', '2026-10-25T09:00:00Z'),
      recentActivity('mines', '2026-10-25T12:00:00Z'),
      recentActivity('mines', '2026-10-25T11:00:00Z'),
    ]
    const slot = SLOT_GAME_CATALOG.find(game => game.id === 'cosmic-fortune')!
    state.slotGames = [{ game: slot, playedAtUtc: '2026-10-25T13:00:00Z' }]
    const markup = renderToStaticMarkup(createElement(HomePage))
    const spotlight = markup.slice(markup.indexOf('home-lobby__spotlights'), markup.indexOf('home-lobby__games'))
    expect(spotlight).toContain(slot.shortTitle)
    expect(spotlight).toContain('Last played')
    const recentRow = markup.slice(markup.indexOf('home-lobby__game-grid'), markup.indexOf('home-lobby__discover'))
    expect(recentRow.indexOf(slot.shortTitle)).toBeLessThan(recentRow.indexOf('mines'))
    expect(recentRow.match(/href="\/games\/mines"/g)).toHaveLength(1)
    expect(recentRow.match(/home-lobby__game-card/g)).toHaveLength(3)
  })

  it('keeps active card sessions resumable', () => {
    state.activities = [{ ...recentActivity('texas-holdem', '2026-10-25T12:00:00Z'), completedAtUtc: null, summary: 'Your table is ready.' }]
    const markup = renderToStaticMarkup(createElement(HomePage))
    expect(markup).toContain('href="/cards/texas-holdem"')
    expect(markup).toContain('Your table is ready.')
    expect(markup).toContain('Continue')
  })

  it('respects hidden games across recent, featured and discovery cards', () => {
    state.activities = [recentActivity('mines', '2026-10-25T12:00:00Z')]
    state.hiddenRoutes = ['/games/mines', '/cards/blackjack', '/games/dragon-tiger']
    const markup = renderToStaticMarkup(createElement(HomePage))
    for (const route of state.hiddenRoutes) expect(markup).not.toContain(`href="${route}"`)
    expect(markup).toContain('Featured today')
    expect(markup).not.toContain('Recently played')
  })

  it('uses the approved hero once and complete title-card image dimensions', () => {
    const markup = renderToStaticMarkup(createElement(HomePage))
    expect(markup.match(/home-lobby__hero-art/g)).toHaveLength(1)
    expect(markup).toContain('width="768" height="384"')
    expect(markup).not.toContain('assets/cards/previews')
    expect(markup).not.toContain('How it works')
  })

  it('keeps loading and authentication errors visible without fake account information', () => {
    state.signedOut = true; state.loading = true
    let markup = renderToStaticMarkup(createElement(HomePage))
    expect(markup).toContain('Opening your account…')
    expect(markup).not.toContain('Your balance')
    state.loading = false; state.error = 'Your session needs attention.'
    markup = renderToStaticMarkup(createElement(HomePage))
    expect(markup).toContain('Your session needs attention.')
    expect(markup).toContain('Try again')
    expect(markup).toContain('href="/login"')
  })

  it('searches the full listed collection and supports category filtering on the page', async () => {
    const host = document.createElement('div'); document.body.append(host)
    const root = createRoot(host); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
    try {
      await act(async () => root.render(<HomePage />))
      const input = host.querySelector<HTMLInputElement>('input[type="search"]')!
      await act(async () => {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'baccarat')
        input.dispatchEvent(new Event('input', { bubbles: true }))
      })
      expect(host.querySelector('#home-lobby-games-title')?.textContent).toBe('Search results')
      expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(1)
      expect(host.querySelector('.home-lobby__game-card')?.getAttribute('href')).toBe('/games/baccarat')
      await act(async () => {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '')
        input.dispatchEvent(new Event('input', { bubbles: true }))
        Array.from(host.querySelectorAll<HTMLButtonElement>('.home-lobby__filters button')).find(button => button.textContent === 'Puzzles')!.click()
      })
      expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(2)
      expect(host.querySelector('.home-lobby__game-grid')?.textContent).toContain('2048')
      expect(host.querySelector('.home-lobby__game-grid')?.textContent).toContain('Drop Merge')
      expect(host.querySelector('.home-lobby__game-grid')?.textContent).not.toContain('Mines')
    } finally { await act(async () => root.unmount()); host.remove() }
  })
})

function recentActivity(game: CardRoomActivity['game'] | 'mines', time: string): LobbyHistoryActivity {
  return {
    id: `${game}-${time}`, matchId: `${game}-${time}`, game, gameLabel: game,
    title: `${game} game`, summary: 'Completed', startedAtUtc: time, completedAtUtc: time,
    unseen: false, requiresClaim: false, winningsCredits: 0,
  }
}

import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { CardRoomActivity } from '../../games/cards/shared/cardRoomHistoryTypes'

const state = vi.hoisted(() => ({ activities: [] as CardRoomActivity[] }))
afterEach(() => { state.activities = [] })

vi.mock('../../components/PlayerHeader', () => ({
  PlayerHeader: () => <header>Player menu</header>,
}))

vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({
    account: {
      userId: 'welcome-test',
      playerName: 'Avery',
      email: 'avery@example.test',
      createdAtUtc: '2026-09-12T00:00:00Z',
      balances: { slotsCredits: 100, freeGames: 0 },
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
    error: null,
    isLoading: false,
    reload: () => undefined,
  }),
}))

vi.mock('../../games/slots/useRecentSlotGame', () => ({
  useRecentSlotGame: () => ({ game: null, playedAtUtc: null }),
}))

vi.mock('../../features/catalog/catalogVisibility', () => ({
  useCatalogueVisibility: () => ({ isVisible: () => true }),
}))

vi.mock('../cards/useCardRoomHistory', () => ({
  useCardRoomHistory: () => ({ activities: state.activities }),
}))

import { HomePage } from './AccountHomePage'

describe('account home', () => {
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
  it('welcomes the player and presents the three compact dashboard cards', () => {
    const markup = renderToStaticMarkup(createElement(HomePage))

    expect(markup).toContain('Welcome, Avery')
    expect(markup).toContain('player-home-dashboard')
    expect(markup.match(/class="player-home-card /g)).toHaveLength(3)
    expect(markup).toContain('Featured today')
  })
})

import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'

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

vi.mock('../cards/useCardRoomHistory', () => ({
  useCardRoomHistory: () => ({ activities: [] }),
}))

import { HomePage } from './AccountHomePage'

describe('account home', () => {
  it('welcomes the player and presents the three compact dashboard cards', () => {
    const markup = renderToStaticMarkup(createElement(HomePage))

    expect(markup).toContain('Welcome, Avery')
    expect(markup).toContain('player-home-dashboard')
    expect(markup.match(/class="player-home-card /g)).toHaveLength(3)
    expect(markup).toContain('Featured today')
  })
})

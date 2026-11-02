import { renderToStaticMarkup } from 'react-dom/server'
import { expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'

vi.mock('../../components/PlayerHeader', () => ({ PlayerHeader: () => <header>Player menu</header> }))
vi.mock('../../features/catalog/catalogVisibility', () => ({ useCatalogueVisibility: () => ({ isVisible: () => true }) }))
vi.mock('../../games/slots/useRecentSlotGame', () => ({ useRecentSlotGame: () => ({ games: [] }) }))
vi.mock('../cards/useCardRoomHistory', () => ({ useCardRoomHistory: () => ({ activities: [{
  id: 'mines-1', matchId: 'mines-round-1', game: 'mines', gameLabel: 'Mines', title: 'Mines game',
  summary: 'R1.02 won', startedAtUtc: '2026-10-25T12:00:00Z', completedAtUtc: '2026-10-25T12:01:00Z',
}] }) }))
import { OtherGamesPage } from './OtherGamesPage'

it('shows Mines under Recently played without falling back to Solitaire', () => {
  const account: AccountSummary = {
    userId: 'mines-history-player', playerName: 'Player', email: 'player@example.test', role: 'player',
    createdAtUtc: '2026-10-25T00:00:00Z', balances: { slotsCredits: 100, freeGames: 0 },
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  }
  const markup = renderToStaticMarkup(<OtherGamesPage account={account} />)
  const recent = markup.match(/<section class="all-games-section" aria-labelledby="all-games-recently-played">[\s\S]*?<\/section>/)?.[0]
  expect(recent).toBeDefined()
  expect(recent).toContain('href="/games/mines"')
  expect(recent).toContain('R1.02 won')
  expect(recent).not.toContain('/cards/solitaire')
  expect(markup).not.toContain('Practice only')
})

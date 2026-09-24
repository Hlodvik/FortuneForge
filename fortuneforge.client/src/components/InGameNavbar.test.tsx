import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { InGameNavbar, InGameNavExits } from './InGameNavbar'

const account: AccountSummary = {
  userId: 'player-1',
  playerName: 'Player One',
  email: 'player@example.test',
  createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 1250, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  role: 'Player',
}

describe('InGameNavbar', () => {
  it('always supplies the canonical home and game-library exits', () => {
    const markup = renderToStaticMarkup(<InGameNavbar account={account} title="Asteroids" />)
    expect(markup).toContain('href="/home"')
    expect(markup).toContain('Fortune Forge home')
    expect(markup).toContain('href="/games"')
    expect(markup).toContain('Other Games')
  })

  it('routes public demos to the public home and demo library', () => {
    const markup = renderToStaticMarkup(<InGameNavExits authenticated={false} />)
    expect(markup).toContain('href="/"')
    expect(markup).toContain('href="/demo"')
    expect(markup).not.toContain('href="/home"')
    expect(markup).not.toContain('href="/games"')
  })

  it('keeps the existing balance and account affordances when an account is supplied', () => {
    const markup = renderToStaticMarkup(<InGameNavbar account={account} title="Snake" />)
    expect(markup).toContain('1,250 South African rand')
    expect(markup).toContain('aria-label="Account menu"')
    expect(markup).toContain('Account settings')
  })
})

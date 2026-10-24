import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { InGameNavbar, InGameNavExits } from './InGameNavbar'
import { PlayerHeader } from './PlayerHeader'

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
  it('provides an accessible report control to signed-in players, but not public demos', () => {
    const markup = renderToStaticMarkup(<InGameNavbar account={account} title="Dragon Tiger" />)
    expect(markup).toContain('aria-label="Report this game"')
    expect(markup).toContain('aria-haspopup="dialog"')
    expect(renderToStaticMarkup(<InGameNavbar title="Public demo" />)).not.toContain('Report this game')
  })
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

  it.each(['arcade', 'casino', 'cards', 'slots'] as const)('omits the balance from the %s game navbar while keeping account actions', theme => {
    const markup = renderToStaticMarkup(<InGameNavbar account={account} title="Game" theme={theme} />)
    expect(markup).not.toContain('South African rand')
    expect(markup).not.toContain('player-shell-menu--balance')
    expect(markup).not.toContain('Balance actions')
    expect(markup).toContain('aria-label="Account menu"')
    expect(markup).toContain('Account settings')
    expect(markup).toContain('Recharge')
    expect(markup).toContain('href="/home/rand#withdrawal-request"')
    expect(markup).toContain('href="/home/invoices"')
    expect(markup).toContain('History')
    expect(markup).toContain('Log out')
  })

  it('keeps the admin portal and admin invoices accessible without a navbar balance', () => {
    const markup = renderToStaticMarkup(<InGameNavbar account={{ ...account, role: 'Admin' }} />)
    expect(markup).not.toContain('player-shell-menu--balance')
    expect(markup).toContain('href="/admin"')
    expect(markup).toContain('href="/admin/invoices"')
    expect(markup).toContain('Customer invoices')
    expect(markup).not.toContain('href="/home/invoices"')
  })

  it('leaves the non-game account header balance and its actions unchanged', () => {
    const markup = renderToStaticMarkup(<PlayerHeader account={account} />)
    expect(markup).toContain('player-shell-menu--balance')
    expect(markup).toContain('1,250 South African rand')
    expect(markup).toContain('Balance actions')
    expect(markup).toContain('Add Rand')
    expect(markup).toContain('Request withdrawal')
    expect(markup).toContain('Invoices')
    expect(markup).toContain('aria-label="Account menu"')
  })
})

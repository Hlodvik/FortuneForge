// @vitest-environment jsdom
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { InGameNavbar } from './InGameNavbar'
import { InGameShell } from './InGameShell'
import { PlayerHeader } from './PlayerHeader'
import { HomeSidebar, SiteNavbar, type SiteNavbarProps } from './SiteNavbar'

const account: AccountSummary = {
  userId: 'player-1',
  playerName: 'Player One',
  email: 'player@example.test',
  createdAtUtc: '2026-01-01T00:00:00Z',
  balances: { slotsCredits: 1250, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  role: 'Player',
}

function dom(markup: string) { const host = document.createElement('div'); host.innerHTML = markup; return host }

function expectBalance(markup: string, full: string, whole: string) {
  const balances = dom(markup).querySelectorAll('.site-header-balance')
  expect(balances).toHaveLength(1)
  expect(balances[0].getAttribute('aria-label')).toBe(`Balance ${full}. Add funds`)
  expect(balances[0].querySelector('.site-header-balance__full')?.textContent).toBe(full)
  expect(balances[0].querySelector('.site-header-balance__whole')?.textContent).toBe(whole)
  expect(balances[0].querySelector('.site-header-balance__whole')?.getAttribute('aria-hidden')).toBe('true')
}

describe('InGameNavbar', () => {
  it('keeps help and reporting in the game surface outside the shared navbar', () => {
    expect(renderToStaticMarkup(<InGameNavbar account={account} title="Dragon Tiger" />)).not.toContain('Report this game')
    const markup = renderToStaticMarkup(<InGameShell account={account} title="Dragon Tiger" supportControls={<button type="button">Game help</button>}><main>Table</main></InGameShell>)
    const navbar = markup.slice(0, markup.indexOf('</header>'))
    expect(navbar).not.toContain('Report this game')
    expect(navbar).not.toContain('Game help')
    const controls = markup.split('class="game-support-controls"')[1]
    expect(controls).toContain('Game help')
    expect(controls).toContain('aria-label="Report this game"')
    expect(controls).toContain('aria-haspopup="dialog"')
    expect(markup.match(/aria-label="Report this game"/g)).toHaveLength(1)
    expect(renderToStaticMarkup(<InGameNavbar title="Public demo" />)).not.toContain('Report this game')
    expect(renderToStaticMarkup(<InGameShell title="Public demo"><main>Table</main></InGameShell>)).not.toContain('game-support-controls')
  })
  it('always supplies home and its game catalogue as the authenticated exits', () => {
    const markup = renderToStaticMarkup(<InGameNavbar account={account} title="Asteroids" />)
    expect(markup).toContain('href="/home"')
    expect(markup).toContain('Fortune Forge home')
    expect(markup).toContain('href="/home#home-lobby-games-title"')
    expect(markup).not.toContain('href="/games"')
    expect(markup).toContain('Other Games')
  })

  it('routes public demos to the public home and demo library', () => {
    const markup = renderToStaticMarkup(<InGameNavbar authenticated={false} />)
    expect(markup).toContain('href="/"')
    expect(markup).toContain('href="/demo"')
    expect(markup).not.toContain('href="/home"')
    expect(markup).not.toContain('href="/games"')
  })

  it.each(['arcade', 'casino', 'cards', 'slots'] as const)('keeps legacy %s input from changing shared game chrome', theme => {
    const legacy = { account, title: 'Nebula Table', theme }
    const markup = renderToStaticMarkup(<InGameNavbar {...legacy} />)
    expect(markup).toBe(renderToStaticMarkup(<InGameNavbar account={account} title="Nebula Table" />))
    expect(markup).not.toContain('Nebula Table')
    expect(markup).not.toContain('player-shell-menu--balance')
    expect(markup).not.toContain('Balance actions')
    expectBalance(markup, 'R1,250.00', 'R1,250')
    expect(markup).toContain('site-navbar__avatar-letter')
    expect(markup).toContain('aria-hidden="true">P</span>')
    expect(markup.indexOf('site-header-balance')).toBeLessThan(markup.indexOf('site-navbar__avatar-account'))
    expect(markup).toContain('aria-label="Account menu"')
    expect(markup).toContain('Account settings')
    expect(markup).toContain('Recharge')
    expect(markup).toContain('href="/balance#withdrawal-request"')
    expect(markup).toContain('href="/home/invoices"')
    expect(markup).toContain('History')
    expect(markup).toContain('Log out')
  })

  it('keeps the admin portal and admin invoices accessible from the avatar menu', () => {
    const markup = renderToStaticMarkup(<InGameNavbar account={{ ...account, role: 'Admin' }} />)
    expect(markup).not.toContain('player-shell-menu--balance')
    expect(markup).toContain('R1,250.00')
    expect(markup).toContain('href="/admin"')
    expect(markup).toContain('href="/admin/invoices"')
    expect(markup).toContain('Customer invoices')
    expect(markup).not.toContain('href="/home/invoices"')
  })

  it('uses an updated live balance when the game passes one', () => {
    const markup = renderToStaticMarkup(<InGameNavbar account={account} balance={2345.67} />)
    expect(markup).toContain('R2,345.67')
    expect(markup).not.toContain('R1,250.00')
  })

  it('uses the same initial-triggered account menu on Home and in games', () => {
    const home = renderToStaticMarkup(<SiteNavbar mode="home" account={account} />)
    const game = renderToStaticMarkup(<InGameNavbar account={account} />)
    for (const markup of [home, game]) {
      expect(markup).toContain('site-navbar__avatar-account')
      expect(markup).toContain('site-navbar__avatar-letter')
      expect(markup).toContain('aria-hidden="true">P</span>')
      expect(markup).toContain('<summary aria-label="Account menu"')
      expect(markup).toContain('<nav aria-label="Account menu"')
    }
  })

  it('uses the Home balance treatment in non-game headers and keeps account actions reachable', () => {
    const markup = renderToStaticMarkup(<PlayerHeader account={account} />)
    expect(markup).not.toContain('player-shell-menu--balance')
    expect(markup).toContain('class="site-header-balance"')
    expectBalance(markup, 'R1,250.00', 'R1,250')
    expect(markup).toContain('href="/balance"')
    expect(markup).toContain('Request withdrawal')
    expect(markup).toContain('Invoices')
    expect(markup).toContain('aria-label="Account menu"')
  })

  it('keeps theme classes and local game controls below the header boundary', () => {
    const host = dom(renderToStaticMarkup(<InGameShell account={account} className="game-owned-theme" bodyClassName="game-owned-body"><button>Local game action</button></InGameShell>))
    const navbar = host.querySelector('[data-game-navbar]')!
    expect(host.querySelectorAll('[data-site-navbar]')).toHaveLength(1)
    expect(navbar.parentElement?.className).toBe('in-game-shell')
    expect(navbar.closest('.game-owned-theme,[data-game-surface]')).toBeNull()
    expect(host.querySelector('[data-game-surface].game-owned-theme .game-owned-body button')?.textContent).toBe('Local game action')
    expect(navbar.textContent).not.toContain('Local game action')
  })

  it('ignores runtime attempts to inject game markup or styling into SiteNavbar', () => {
    const unsafe = { mode: 'game', account, className: 'game-hijack', theme: 'slots', actions: <button>Injected game control</button> } as unknown as SiteNavbarProps
    const markup = renderToStaticMarkup(<SiteNavbar {...unsafe} />)
    expect(markup).toBe(renderToStaticMarkup(<SiteNavbar mode="game" account={account} />))
    expect(markup).not.toContain('game-hijack')
    expect(markup).not.toContain('Injected game control')
  })

  it('keeps a provided public-demo balance read-only and preserves shared demo exits', () => {
    const host = dom(renderToStaticMarkup(<InGameNavbar balance={10000} />))
    expect(host.querySelector('.site-header-balance')?.tagName).toBe('SPAN')
    expect(host.querySelector('.site-header-balance')?.getAttribute('aria-label')).toBe('Balance R10,000.00')
    expect(host.querySelector('[aria-label="Demo player"]')).not.toBeNull()
    expect(host.querySelector('[aria-label="Account menu"],[aria-label="Report this game"]')).toBeNull()
    expect(host.querySelector('[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/')
    expect(host.querySelector('.player-shell-header__games')?.getAttribute('href')).toBe('/demo')
  })

  it('orders Account, Add Funds and History in the sidebar and preserves the other exits', () => {
    const host = dom(renderToStaticMarkup(<HomeSidebar activeNav="home" onHelp={() => undefined} />))
    const links = [...host.querySelectorAll('nav[aria-label="Player navigation"] a')]
    expect(links.map(link => link.textContent)).toEqual(['Account', 'Add Funds', 'History'])
    expect(links.map(link => link.getAttribute('href'))).toEqual(['/home/settings', '/balance', '/home/history'])
    expect(host.querySelector('[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/home')
    expect(host.querySelector('[aria-label="Help"]')).not.toBeNull()
    expect(dom(renderToStaticMarkup(<InGameNavbar account={account} />)).querySelector('.player-shell-header__games')).not.toBeNull()
    expect(dom(renderToStaticMarkup(<SiteNavbar mode="public" />)).querySelector('a[href="/#discover-games"]')).not.toBeNull()
  })
})

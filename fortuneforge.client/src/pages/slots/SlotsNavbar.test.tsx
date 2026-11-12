// @vitest-environment jsdom
import { renderToStaticMarkup } from 'react-dom/server'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { loadAllSlotGameManifests } from '../../games/slots/routeRegistry'
import { SlotsPage } from './SlotsPage'
import { RainbowRealmMachinePreview } from './RainbowRealmMachinePreview'

vi.mock('../../features/slots/hooks/usePrefersReducedMotion', () => ({ usePrefersReducedMotion: () => true }))
// Keep the Pirates' Fortune scene portal in this render; navigation is the boundary under test.
vi.mock('react-dom', async importOriginal => ({
  ...await importOriginal<typeof import('react-dom')>(),
  createPortal: (children: ReactNode) => children,
}))

const games = await loadAllSlotGameManifests()
const account = {
  userId: 'slot-navbar-player', playerName: 'Slot Player', email: 'slots@example.test',
  createdAtUtc: '2026-10-31T00:00:00Z', role: 'player',
  balances: { slotsCredits: 1234.56, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}

describe.each(games)('$catalog.title site navigation', game => {
  it.each([true, false])('keeps one canonical navbar and usable game controls in demo mode %s', demoMode => {
    const host = document.createElement('div')
    host.innerHTML = renderToStaticMarkup(<SlotsPage
      demoMode={demoMode}
      account={demoMode ? undefined : account}
      experienceSet={game.experience}
    />)
    const navbars = host.querySelectorAll('[data-site-navbar]')
    expect(navbars).toHaveLength(1)
    expect(host.querySelector('.slots-page__topbar')).toBeNull()
    const navbar = navbars[0]
    expect(navbar.classList.contains('slots-page__navbar')).toBe(false)
    expect(navbar.querySelector('.slots-page__navbar-actions')).toBeNull()
    expect(navbar.querySelector('button[aria-label="How to win"]')).toBeNull()
    expect(navbar.querySelector('button[aria-label="Open settings"]')).toBeNull()
    expect(navbar.textContent).not.toContain(game.experience.cabinet.title)
    expect(navbar.querySelector('[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe(demoMode ? '/' : '/home')
    expect(navbar.querySelector('.player-shell-header__games')?.getAttribute('href')).toBe(demoMode ? '/#discover-games' : '/home')
    const balance = navbar.querySelector('.site-header-balance')!
    expect(navbar.querySelectorAll('.site-header-balance')).toHaveLength(1)
    expect(balance.tagName).toBe(demoMode ? 'SPAN' : 'A')
    expect(balance.getAttribute('href')).toBe(demoMode ? null : '/balance')
    expect(balance.getAttribute('aria-label')).toBe(demoMode ? 'Balance R10,000.00' : 'Balance R1,234.56. Add funds')
    expect(balance.querySelector('.site-header-balance__full')?.textContent).toBe(demoMode ? 'R10,000.00' : 'R1,234.56')
    expect(balance.querySelector('.site-header-balance__whole[aria-hidden="true"]')?.textContent).toBe(demoMode ? 'R10,000' : 'R1,234')
    expect(navbar.querySelector('[aria-label="Account menu"]') !== null).toBe(!demoMode)
    expect(navbar.querySelector('.site-navbar__avatar-letter')?.textContent).toBe(demoMode ? 'D' : 'S')
    expect(navbar.querySelector('[aria-label="Demo player"]') !== null).toBe(demoMode)
    expect(navbar.querySelector('button[aria-label="Report this game"]')).toBeNull()
    expect(host.querySelector('.game-support-controls button[aria-label="Report this game"]') !== null).toBe(!demoMode)
    if (!demoMode) {
      const balance = navbar.querySelector('.site-header-balance')
      const avatar = navbar.querySelector('.site-navbar__avatar-letter')
      expect(balance && avatar && (balance.compareDocumentPosition(avatar) & Node.DOCUMENT_POSITION_FOLLOWING)).toBeTruthy()
    }
    expect(navbar.querySelector('[aria-label^="Payment alerts"]')).toBeNull()
    expect(host.querySelector('[aria-label^="Payment alerts"]') !== null).toBe(!demoMode)
    expect(host.querySelectorAll('button[aria-label="How to win"]')).toHaveLength(1)
    expect(host.querySelectorAll('button[aria-label="Open settings"]')).toHaveLength(1)
    expect(host.querySelector('h1')?.textContent).toBe(game.experience.cabinet.title)
  })
})

it('gives the public Rainbow Realm preview canonical public game exits', () => {
  const host = document.createElement('div')
  host.innerHTML = renderToStaticMarkup(<RainbowRealmMachinePreview />)
  expect(host.querySelectorAll('[data-site-navbar]')).toHaveLength(1)
  expect(host.querySelector('[aria-label="Fortune Forge home"]')?.getAttribute('href')).toBe('/')
  expect(host.querySelector('.player-shell-header__games')?.getAttribute('href')).toBe('/#discover-games')
  expect(host.querySelector('[data-site-navbar]')?.textContent).not.toContain('Rainbow Realm Preview')
  expect(host.querySelector('.landing-bar')).toBeNull()
})

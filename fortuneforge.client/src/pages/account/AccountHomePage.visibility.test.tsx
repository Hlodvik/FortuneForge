// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CardRoomActivity } from '../../games/cards/shared/cardRoomHistoryTypes'
import { notifyCatalogueChanged } from '../../features/catalog/catalogVisibility'

const state = vi.hoisted(() => ({
  cards: [] as CardRoomActivity[],
  recentSlot: null as null | { playHref: string; image: string; title: string; shortTitle: string },
  slotPlayedAt: null as string | null,
}))

vi.mock('../../components/PlayerHeader', () => ({
  PlayerAccountMenus: ({ showBalance }: { showBalance: boolean }) => <div data-testid="account-menu" data-show-balance={showBalance}>Player menu</div>,
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: { userId: 'visibility-player', playerName: 'Avery', balances: { slotsCredits: 100, freeGames: 0 } }, error: null, isLoading: false, reload: vi.fn(),
}) }))
vi.mock('../../games/slots/useRecentSlotGame', () => ({ useRecentSlotGame: () => ({ game: state.recentSlot, playedAtUtc: state.slotPlayedAt }) }))
vi.mock('../cards/useCardRoomHistory', () => ({ useCardRoomHistory: () => ({ activities: state.cards }) }))
vi.mock('../../games/slots/catalog', () => ({ SLOT_GAME_CATALOG: [
  { id: 'rainbow-realm', playHref: '/slots/rainbow-realm', image: '/rainbow.png', shortTitle: 'Rainbow Realm' },
  { id: 'gods-of-olympus', playHref: '/slots/gods-of-olympus', image: '/olympus.png', shortTitle: 'Gods of Olympus' },
  { id: 'pirates-fortune', playHref: '/slots/pirates-fortune', image: '/pirates.png', shortTitle: 'Pirates Fortune' },
  { id: 'wukong-journey-to-the-west', playHref: '/slots/wukong-journey-to-the-west', image: '/wukong.png', shortTitle: 'Wukong' },
] }))

import { HomePage } from './AccountHomePage'

const featuredRoutes = ['/cards/blackjack', '/slots/rainbow-realm', '/cards/solitaire', '/slots/gods-of-olympus', '/cards/texas-holdem', '/slots/pirates-fortune', '/slots/wukong-journey-to-the-west']
let version = 0
let host: HTMLDivElement
let root: Root
function visible(...routes: string[]) {
  notifyCatalogueChanged({ version: ++version, games: featuredRoutes.map(route => ({ id: route, route, visible: routes.includes(route) })) })
}
const recentCard = (): CardRoomActivity => ({
  id: 'result-1', matchId: 'match-1', game: 'blackjack', gameLabel: 'Blackjack', title: 'Blackjack game',
  summary: 'Result ready', startedAtUtc: '2026-10-21T12:00:00Z', completedAtUtc: '2026-10-21T12:10:00Z',
  unseen: false, requiresClaim: false, winningsCredits: 0,
})

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('BroadcastChannel', undefined)
  vi.stubGlobal('fetch', vi.fn())
  state.cards = []
  state.recentSlot = null
  state.slotPlayedAt = null
  visible()
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
const render = () => act(async () => { root.render(<HomePage />) })
const highlight = () => host.querySelector<HTMLAnchorElement>('.home-lobby__continue')!

describe('home catalogue visibility', () => {
  it('features only a visible game rather than a hidden daily selection', async () => {
    visible('/cards/solitaire')
    await render()
    expect(highlight().getAttribute('href')).toBe('/cards/solitaire')
    expect(highlight().textContent).toContain('Featured today')
    expect(host.querySelector('.home-lobby__game-grid')?.textContent).toContain('Competitive Solitaire')
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(1)
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
  })

  it('does not link a hidden recent card game and substitutes an available featured game', async () => {
    state.cards = [recentCard()]
    visible('/cards/solitaire')
    await render()
    expect(highlight().getAttribute('href')).toBe('/cards/solitaire')
    expect(highlight().textContent).toContain('Featured today')
    expect(highlight().textContent).not.toContain('Recently played')
    expect(highlight().textContent).not.toContain('Last played')
    expect(host.querySelector('#home-lobby-games-title')?.textContent).toBe('Find your next favourite')
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
  })

  it('does not link a hidden recent slot even when it is newer than card history', async () => {
    state.cards = [recentCard()]
    state.recentSlot = { playHref: '/slots/rainbow-realm', image: '/rainbow.png', title: 'Rainbow Realm', shortTitle: 'Rainbow Realm' }
    state.slotPlayedAt = '2026-10-21T13:00:00Z'
    visible('/cards/solitaire')
    await render()
    expect(highlight().getAttribute('href')).toBe('/cards/solitaire')
    expect(highlight().textContent).toContain('Featured today')
    expect(host.querySelector('a[href="/slots/rainbow-realm"]')).toBeNull()
  })

  it('falls back to Browse when both recent and featured games are hidden', async () => {
    state.cards = [recentCard()]
    await render()
    expect(highlight().textContent).toContain('Find your favourite')
    expect(highlight().textContent).toContain('Browse games')
    expect(highlight().getAttribute('href')).toBe('/games')
    expect(host.querySelectorAll('a[href^="/cards/"], a[href^="/slots/"]')).toHaveLength(0)
    expect(host.querySelectorAll('.home-lobby__game-card, .home-lobby__discovery')).toHaveLength(0)
  })

  it('removes a visible recent-game link immediately when the admin hides it', async () => {
    state.cards = [recentCard()]
    visible('/cards/blackjack')
    await render()
    expect(highlight().textContent).toContain('Last played')
    expect(host.querySelector('#home-lobby-games-title')?.textContent).toBe('Recently played')
    expect(highlight().getAttribute('href')).toBe('/cards/blackjack')

    act(() => visible())
    expect(highlight().getAttribute('href')).toBe('/games')
    expect(highlight().textContent).toContain('Browse games')
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('uses the next visible real history entry when the newest slot is hidden', async () => {
    state.cards = [recentCard()]
    state.recentSlot = { playHref: '/slots/rainbow-realm', image: '/rainbow.png', title: 'Rainbow Realm', shortTitle: 'Rainbow Realm' }
    state.slotPlayedAt = '2026-10-21T13:00:00Z'
    visible('/cards/blackjack', '/cards/solitaire')
    await render()
    expect(highlight().getAttribute('href')).toBe('/cards/blackjack')
    expect(highlight().textContent).toContain('Last played')
    expect(highlight().textContent).not.toContain('Featured today')
    expect(host.querySelector('.home-lobby__game-grid')?.textContent).toContain('Blackjack')
    expect(host.querySelector('a[href="/slots/rainbow-realm"]')).toBeNull()
  })

  it('updates filtered search results immediately when catalogue visibility changes', async () => {
    visible('/cards/blackjack', '/cards/solitaire')
    await render()
    const input = host.querySelector<HTMLInputElement>('input[type="search"]')!
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'blackjack')
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(1)
    expect(host.querySelector('.home-lobby__game-card')?.getAttribute('href')).toBe('/cards/blackjack')

    act(() => visible('/cards/solitaire'))
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(0)
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
    expect(host.textContent).toContain('No games match')

    act(() => visible('/cards/blackjack', '/cards/solitaire'))
    expect(host.querySelectorAll('.home-lobby__game-card')).toHaveLength(1)
    expect(host.querySelector('.home-lobby__game-card')?.getAttribute('href')).toBe('/cards/blackjack')
    expect(fetch).not.toHaveBeenCalled()
  })
})

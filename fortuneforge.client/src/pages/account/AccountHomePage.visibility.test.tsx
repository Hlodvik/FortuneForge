// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CardRoomActivity } from '../../games/cards/shared/cardRoomHistoryTypes'
import { notifyCatalogueChanged } from '../../features/catalog/catalogVisibility'

const state = vi.hoisted(() => ({
  cards: [] as CardRoomActivity[],
  recentSlot: null as null | { playHref: string; image: string; title: string },
  slotPlayedAt: null as string | null,
}))

vi.mock('../../components/PlayerHeader', () => ({ PlayerHeader: () => <header>Player menu</header> }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: { userId: 'visibility-player', playerName: 'Avery' }, error: null, isLoading: false, reload: vi.fn(),
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
const highlight = () => host.querySelector('.player-home-card--recent')!

describe('home catalogue visibility', () => {
  it('features only a visible game rather than a hidden daily selection', async () => {
    visible('/cards/solitaire')
    await render()
    expect(highlight().querySelector('a')?.getAttribute('href')).toBe('/cards/solitaire')
    expect(highlight().textContent).toContain('Featured today')
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
  })

  it('does not link a hidden recent card game and substitutes an available featured game', async () => {
    state.cards = [recentCard()]
    visible('/cards/solitaire')
    await render()
    expect(highlight().querySelector('a')?.getAttribute('href')).toBe('/cards/solitaire')
    expect(highlight().textContent).not.toContain('Recently played')
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
  })

  it('does not link a hidden recent slot even when it is newer than card history', async () => {
    state.cards = [recentCard()]
    state.recentSlot = { playHref: '/slots/rainbow-realm', image: '/rainbow.png', title: 'Rainbow Realm' }
    state.slotPlayedAt = '2026-10-21T13:00:00Z'
    visible('/cards/solitaire')
    await render()
    expect(highlight().querySelector('a')?.getAttribute('href')).toBe('/cards/solitaire')
    expect(host.querySelector('a[href="/slots/rainbow-realm"]')).toBeNull()
  })

  it('falls back to Browse when both recent and featured games are hidden', async () => {
    state.cards = [recentCard()]
    await render()
    expect(highlight().textContent).toContain('Find your next game')
    expect(highlight().textContent).toContain('Browse games')
    expect(highlight().querySelector('a')?.getAttribute('href')).toBe('/games')
    expect(host.querySelectorAll('a[href^="/cards/"], a[href^="/slots/"]')).toHaveLength(0)
  })

  it('removes a visible recent-game link immediately when the admin hides it', async () => {
    state.cards = [recentCard()]
    visible('/cards/blackjack')
    await render()
    expect(highlight().textContent).toContain('Recently played')
    expect(highlight().querySelector('a')?.getAttribute('href')).toBe('/cards/blackjack')

    act(() => visible())
    expect(highlight().querySelector('a')?.getAttribute('href')).toBe('/games')
    expect(highlight().textContent).toContain('Browse games')
    expect(host.querySelector('a[href="/cards/blackjack"]')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })
})

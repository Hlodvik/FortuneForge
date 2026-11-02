// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getCataloguePresentationArt } from '../features/catalog/cataloguePresentationArt'
import { SLOT_GAME_CATALOG } from '../games/slots/catalog'
import { OtherGamesPage } from './games/OtherGamesPage'
import { DemoSlotsLibraryPage, SlotsLibraryPage } from './slots/SlotsLibraryPage'

vi.mock('../components/PlayerHeader', () => ({ PlayerHeader: () => null }))
vi.mock('../components/GameTypeMenu', () => ({ GameTypeMenu: () => null }))
vi.mock('../features/catalog/CatalogueStatus', () => ({ CatalogueStatus: () => null }))
vi.mock('../features/catalog/catalogVisibility', () => ({ useCatalogueVisibility: () => ({ isVisible: () => true }) }))
vi.mock('../games/slots/useRecentSlotGame', () => ({ useRecentSlotGame: () => ({ games: [] }) }))
vi.mock('./cards/useCardRoomHistory', () => ({ useCardRoomHistory: () => ({ activities: [] }) }))

const account = {
  userId: 'artwork-test', playerName: 'Artwork Tester', email: 'artwork@example.test',
  createdAtUtc: '2026-10-31T00:00:00Z', role: 'player',
  balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
} as const

const surfaces = [
  { name: 'authenticated slot library', element: () => <SlotsLibraryPage account={account} />, demo: false, slotCard: true },
  { name: 'demo slot library', element: () => <DemoSlotsLibraryPage />, demo: true, slotCard: true },
  { name: 'all-games catalogue', element: () => <OtherGamesPage account={account} />, demo: false, slotCard: false },
] as const

const legacyGame = SLOT_GAME_CATALOG.find((game) => game.id === 'reel-riches')!
let host: HTMLDivElement
let root: Root
let cssWidth: number
let notifyResize: () => void
const observerCreated = vi.fn()

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  cssWidth = 0
  notifyResize = () => undefined
  vi.spyOn(HTMLImageElement.prototype, 'clientWidth', 'get').mockImplementation(() => cssWidth)
  vi.spyOn(HTMLImageElement.prototype, 'clientHeight', 'get').mockImplementation(() => cssWidth / 2)
  vi.stubGlobal('getComputedStyle', () => ({ objectFit: 'cover' }))
  vi.stubGlobal('ResizeObserver', class {
    constructor(callback: () => void) { observerCreated(); notifyResize = callback }
    observe() {}
    disconnect() {}
  })
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})

afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

function card(container: ParentNode, gameId: string, surface: typeof surfaces[number]): Element {
  const href = `/slots/${gameId}${surface.demo ? '/demo' : ''}`
  const link = container.querySelector(`a[href="${href}"]`)
  const result = link?.closest(surface.slotCard ? 'article.machine-card' : 'a.all-games-card')
  expect(result, href).toBeTruthy()
  return result!
}

describe.each(surfaces)('$name responsive title artwork', (surface) => {
  it('withholds Midnight Vault sources before measurement while preserving legacy imagery', () => {
    const markup = renderToStaticMarkup(surface.element())
    const container = document.createElement('div')
    container.innerHTML = markup
    const midnight = card(container, 'dragon-hoard', surface).querySelector('img')!
    expect(midnight.hasAttribute('src')).toBe(false)
    expect(midnight.hasAttribute('srcset')).toBe(false)
    expect(midnight.getAttribute('width')).toBe('768')
    expect(midnight.getAttribute('height')).toBe('384')
    expect(midnight.getAttribute('loading')).toBe('lazy')
    expect(midnight.getAttribute('decoding')).toBe('async')
    const legacy = card(container, 'reel-riches', surface).querySelector('img')!
    expect(legacy.getAttribute('src')).toBe(legacyGame.image)
    expect(legacy.hasAttribute('srcset')).toBe(false)
    expect(legacy.hasAttribute('loading')).toBe(false)
    expect(markup).not.toContain('midnight-vault-catalogue-title-')
    expect(markup).not.toContain('/assets/slots/midnight-vault/v1/symbols/')
    if (surface.slotCard) expect(card(container, 'dragon-hoard', surface).querySelector('.machine-card__art')?.hasAttribute('style')).toBe(false)
  })

  it('exposes candidates only for Midnight Vault after measuring its title box', () => {
    act(() => root.render(surface.element()))
    const midnight = card(host, 'dragon-hoard', surface).querySelector('img')!
    expect(midnight.hasAttribute('src')).toBe(false)
    expect(host.querySelectorAll('img[srcset]')).toHaveLength(0)
    cssWidth = 320
    act(() => notifyResize())
    const artwork = getCataloguePresentationArt(`/slots/dragon-hoard${surface.demo ? '/demo' : ''}`)!
    expect(midnight.getAttribute('src')).toBe(artwork.src)
    expect(midnight.getAttribute('srcset')).toBe(artwork.imageSrcSet)
    expect(midnight.getAttribute('sizes')).toBe('320px')
    expect(host.querySelectorAll('img[srcset]')).toHaveLength(1)
    expect(observerCreated).toHaveBeenCalledOnce()
    const legacy = card(host, 'reel-riches', surface).querySelector('img')!
    expect(legacy.getAttribute('src')).toBe(legacyGame.image)
    expect(legacy.hasAttribute('srcset')).toBe(false)
    expect(host.querySelector('link[rel="preload"]')).toBeNull()
  })
})

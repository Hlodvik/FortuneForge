import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { CardGameLibraryPage } from './cards/CardGameLibraryPage'
import { OtherGamesPage } from './games/OtherGamesPage'
import { DemoSlotsLibraryPage, SlotsLibraryPage } from './slots/SlotsLibraryPage'

const account = {
  userId: 'layout-test',
  playerName: 'Layout Tester',
  email: 'layout@example.test',
  createdAtUtc: '2026-08-15T00:00:00Z',
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
} as const

describe('game category menus', () => {
  it('renders the slot menu with all twenty demo games and a card-room link', () => {
    const markup = renderToStaticMarkup(createElement(DemoSlotsLibraryPage))
    const demoLinks = markup.match(/aria-label="Play demo:/g) ?? []

    expect(demoLinks).toHaveLength(20)
    expect(markup).toContain('aria-label="Game categories"')
    expect(markup).toContain('href="/demo" aria-current="page"')
    expect(markup).toContain('href="/demo/cards"')
    expect(markup).toContain('Slot machines')
    expect(markup).toContain('Card room')
  })

  it('omits ready labels from authenticated slot cards', () => {
    const markup = renderToStaticMarkup(createElement(SlotsLibraryPage, { account }))

    expect(markup.match(/machine-card--slot/g)).toHaveLength(20)
    expect(markup).not.toContain('Ready to play')
  })

  it('keeps card demos internal and unlinked from the category menu', () => {
    const markup = renderToStaticMarkup(createElement(CardGameLibraryPage, { demoMode: true }))

    expect(markup).toContain('href="/demo/cards" aria-current="page"')
    expect(markup).toContain('Choose your card game')
    expect(markup).toContain('Fortune Blackjack')
    expect(markup).not.toContain('href="/demo/cards/blackjack"')
    expect(markup).toContain('Texas Hold&#x27;em')
    expect(markup).not.toContain('href="/demo/cards/texas-holdem"')
    expect(markup).toContain('Competitive Solitaire')
    expect(markup).not.toContain('href="/demo/cards/solitaire"')
    expect(markup.match(/Internal route only/g)).toHaveLength(3)
    expect(markup).not.toContain('bot-practice')
  })

  it('opens the authenticated game browser on All games with grouped sections', () => {
    const markup = renderToStaticMarkup(createElement(OtherGamesPage, { account }))

    expect(markup).toContain('href="/games" aria-current="page"')
    expect(markup).toContain('All games')
    expect(markup).toContain('>Popular</h2>')
    expect(markup).toContain('>Recently played</h2>')
    expect(markup).toContain('>Card games</h2>')
    expect(markup).toContain('>Casino games</h2>')
    expect(markup).toContain('>Arcade games</h2>')
    expect(markup).toContain('>Dice games</h2>')
    expect(markup).toContain('>Etc.</h2>')
    expect(markup).not.toContain('>New games</h2>')
  })
})

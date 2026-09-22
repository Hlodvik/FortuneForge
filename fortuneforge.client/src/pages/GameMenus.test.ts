import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { pageTitleForPath } from '../app/usePageTitle'
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
    expect(markup).toContain('Texas Hold’em')
    expect(markup).not.toContain('href="/demo/cards/texas-holdem"')
    expect(markup).toContain('Competitive Solitaire')
    expect(markup).not.toContain('href="/demo/cards/solitaire"')
    expect(markup.match(/Internal route only/g)).toHaveLength(3)
    expect(markup).not.toContain('bot-practice')
  })

  it('omits recently played when the authenticated game browser has no history', () => {
    const markup = renderToStaticMarkup(createElement(OtherGamesPage, { account }))

    expect(markup).toContain('href="/games" aria-current="page"')
    expect(markup).toContain('All games')
    expect(markup).toContain('>Popular</h2>')
    expect(markup).not.toContain('>Recently played</h2>')
    expect(markup).toContain('>Card games</h2>')
    expect(markup).toContain('>Casino games</h2>')
    expect(markup).toContain('>Arcade games</h2>')
    expect(markup).toContain('>Dice games</h2>')
    expect(markup).toContain('>New games</h2>')
    expect(markup).toContain('>Slot machines</h2>')
    expect(markup).toContain('>20 games</span>')
    expect(markup).not.toContain('>Etc.</h2>')
    expect(markup).toContain('href="/cards/hearts"')
    expect(markup).toContain('href="/games/liars-dice"')
    expect(markup).toContain('href="/games/roulette"')
    expect(markup).toContain('href="/games/craps"')
    expect(markup).toContain('href="/games/asteroids"')
    expect(markup).toContain('href="/games/horse-flight"')
    expect(markup).toContain('href="/games/2048"')
    expect(markup).toContain('href="/games/drop-merge"')
    expect(markup).toContain('all-games-card__image--contain all-games-card__image--compact')
    expect(markup).not.toContain('>☀</span>')
  })

  it('lists the arcade catalogue without placeholder badges', () => {
    const markup = renderToStaticMarkup(createElement(OtherGamesPage, { account }))

    expect(markup).toContain('href="/games/asteroids"')
    expect(markup).toContain('href="/games/flappy"')
    expect(markup).toContain('href="/games/baccarat"')
    expect(markup).toContain('href="/games/video-poker"')
    expect(markup).toContain('Asteroids')
    expect(markup).toContain('Flappy')
    expect(markup).toContain('Horse Flight')
    expect(markup).toContain('endless platform runner')
    expect(markup).toContain('2048')
    expect(markup).toContain('Drop Merge')
    expect(markup).not.toContain('4096')
    expect(markup).not.toContain('Falling Blocks')
    expect(markup).toContain('Video Poker')
    expect(markup).toContain('Baccarat')
    expect(markup).toContain('Casino War')
    expect(markup).toContain('Sic Bo')
    expect(markup).toContain('Keno')
    expect(markup).not.toContain('Shoot ’Em Up!')
    expect(markup).not.toContain('Balut')
    expect(markup).not.toContain('In the forge')
  })

  it('uses the Asteroids competition title', () => {
    expect(pageTitleForPath('/games/asteroids')).toBe('Asteroids Competition — Fortune Forge')
    expect(pageTitleForPath('/games/2048')).toBe('2048 — Fortune Forge')
    expect(pageTitleForPath('/games/drop-merge')).toBe('Drop Merge — Fortune Forge')
    expect(pageTitleForPath('/games/baccarat')).toBe('Baccarat — Fortune Forge')
    expect(pageTitleForPath('/games/flappy')).toBe('Flappy — Fortune Forge')
    expect(pageTitleForPath('/games/video-poker')).toBe('Video Poker — Fortune Forge')
    expect(pageTitleForPath('/games/roulette')).toBe('Roulette — Fortune Forge')
    expect(pageTitleForPath('/games/craps')).toBe('Craps — Fortune Forge')
    expect(pageTitleForPath('/cards/hearts')).toBe('Hearts — Fortune Forge')
  })
})

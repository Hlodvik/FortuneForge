import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { CardRoomHeader } from './CardRoomHeader'

describe('CardRoomHeader', () => {
  it('preserves canonical account exits and keeps game history below the shared navbar', () => {
    const markup = renderToStaticMarkup(
      <CardRoomHeader
        account={{ userId: 'river-fox', playerName: 'RiverFox', email: 'river@example.com', createdAtUtc: '2026-01-01T00:00:00Z', role: 'Player',
          balances: { slotsCredits: 125.5, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } }}
        unseenCount={2}
        historyContent={<p>History content</p>}
      />,
    )

    expect(markup).toContain('aria-label="Fortune Forge home"')
    expect(markup).toContain('aria-label="Game history, 2 new"')
    expect(markup.match(/>Other Games<\/a>/g)).toHaveLength(1)
    const header = markup.match(/<header\b[\s\S]*?<\/header>/)?.[0]
    expect(header).toContain('data-game-navbar="true"')
    expect(header).not.toContain('Game history')
    expect(header).not.toContain('History content')
    expect(markup.indexOf('aria-label="Card room controls"')).toBeGreaterThan(markup.indexOf('</header>'))
    expect(markup).toContain('class="game-local-controls card-room-local-controls"')
    expect(markup).toContain('aria-label="Account menu"')
    expect(markup).toContain('aria-label="Balance R125.50. Add funds"')
    expect(markup).toContain('href="/home/invoices"')
    expect(markup).not.toContain('forge-credit-amount')
    expect(markup).not.toContain('← Card room')
  })
})

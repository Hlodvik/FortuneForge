import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { CardRoomHistory } from './CardRoomHistory'
import { cardRoomUnseenCount, type CardRoomActivity } from './cardRoomHistoryTypes'

const activities: readonly CardRoomActivity[] = [
  {
    id: 'blackjack-active', matchId: 'table-12', game: 'blackjack', gameLabel: 'Blackjack', title: 'Table 12', summary: 'Round 4',
    startedAtUtc: '2026-08-16T12:00:00Z', completedAtUtc: null, unseen: false, requiresClaim: false, winningsCredits: null,
  },
  {
    id: 'solitaire-ready', matchId: 'solitaire-match', game: 'solitaire', gameLabel: 'Solitaire', title: 'Finished run', summary: 'R9.00 won',
    startedAtUtc: '2026-08-16T11:00:00Z', completedAtUtc: '2026-08-16T11:10:00Z', unseen: true, requiresClaim: true, winningsCredits: 9,
  },
  {
    id: 'holdem-seen', matchId: 'holdem-table', game: 'texas-holdem', gameLabel: 'Hold’em', title: 'Table complete', summary: '3 hands',
    startedAtUtc: '2026-08-15T11:00:00Z', completedAtUtc: '2026-08-15T12:00:00Z', unseen: false, requiresClaim: false, winningsCredits: 4,
  },
]

describe('CardRoomHistory', () => {
  it('has an Andar Bahar result group without hiding existing game groups', () => {
    const markup = renderToStaticMarkup(<CardRoomHistory activities={[{
      id: 'andar-bahar-round-1', matchId: 'round-1', game: 'andar-bahar', gameLabel: 'Andar Bahar', title: 'Andar Bahar game', summary: 'R0.95 won',
      startedAtUtc: '2026-10-24T12:00:00Z', completedAtUtc: '2026-10-24T12:00:00Z', unseen: true,
      requiresClaim: false, winningsCredits: .95, wagerCredits: .5, netCredits: .45,
    }]} loading={false} error={null} busyId={null} onSelect={() => undefined} />)
    expect(markup).toContain('Andar Bahar game')
    expect(markup).toContain('R0.95 won')
    expect(markup).toContain('Hi-Lo')
    expect(markup).toContain('House Hold’em')
  })
  it('shows Hi-Lo beside the already-supported single-player card games', () => {
    const markup = renderToStaticMarkup(<CardRoomHistory activities={[{
      id: 'hi-lo-round-1', matchId: 'round-1', game: 'hi-lo', gameLabel: 'Hi-Lo', title: 'Hi-Lo game', summary: 'R2.14 won',
      startedAtUtc: '2026-10-23T12:00:00Z', completedAtUtc: '2026-10-23T12:01:00Z', unseen: true,
      requiresClaim: false, winningsCredits: 2.14, wagerCredits: 1, netCredits: 1.14,
    }]} loading={false} error={null} busyId={null} onSelect={() => undefined} />)
    expect(markup).toContain('Hi-Lo game')
    expect(markup).toContain('R2.14 won')
    expect(markup).toContain('Three Card Poker')
    expect(markup).toContain('House Hold’em')
  })

  it('keeps unseen finished games in Active until they are opened', () => {
    const markup = renderToStaticMarkup(
      <CardRoomHistory activities={activities} loading={false} error={null} busyId={null} onSelect={() => undefined} />,
    )

    expect(markup).toContain('Table 12')
    expect(markup).toContain('Finished run')
    expect(markup).toContain('Table complete')
    expect(markup).toContain('Blackjack')
    expect(markup).toContain('Texas Hold’em')
    expect(markup).toContain('Open &amp; claim')
    expect(cardRoomUnseenCount(activities)).toBe(1)
  })
})

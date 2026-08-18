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

import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { SlotPlayGuide } from './SlotPlayGuide'

const symbolSet = {
  id: 'test',
  definitions: {
    ACE: { id: 'ACE' as const, label: 'Jolly Roger wild', image: '/wild.png' },
    '2': { id: '2' as const, label: 'Gold doubloon', image: '/coin.png' },
  },
  guideEntries: [],
}

describe('SlotPlayGuide', () => {
  it('makes the default winning rules and chest reward clear before the first spin', () => {
    const markup = renderToStaticMarkup(
      <SlotPlayGuide
        bestWin={null}
        collections={{ ariaLabel: 'Treasure chests', entries: [{ id: 'ruby', label: 'Ruby chest', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 15 }] }}
        help={{ paylineCount: 21 }}
        lastWin={0}
        onOpenHelp={() => undefined}
        selectedWager={5}
        specialRound={{ id: 'broadside', title: 'Broadside Runs', earnLabel: 'Fill a chest', activeModes: {} }}
        symbolSet={symbolSet}
        winningPaylineCount={0}
      />,
    )

    expect(markup).toContain('Match 3 or more of the same symbol from the first reel')
    expect(markup).toContain('fill any chest with 15 matching gems for Broadside Runs')
    expect(markup).toContain('View all 21 winning routes')
  })

  it('explains a winning line and promotes a jackpot-sized result', () => {
    const markup = renderToStaticMarkup(
      <SlotPlayGuide
        bestWin={{ paylineId: 4, amountPoints: 500, matches: [{ amountPoints: 500, multiplier: 10, match: { paylineId: 4, symbolId: '2', matchLength: 5, positions: [], wildPositions: [] } }] }}
        help={{ paylineCount: 21 }}
        lastWin={500}
        onOpenHelp={() => undefined}
        selectedWager={5}
        symbolSet={symbolSet}
        winningPaylineCount={2}
      />,
    )

    expect(markup).toContain('Jackpot win')
    expect(markup).toContain('Line 4 paid for 5 Gold doubloons in a row. Plus 1 more winning line.')
  })
})

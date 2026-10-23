// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ArcadeCompetitionLeaderboard } from './ArcadeCompetitionLeaderboard'
import type { ArcadeCompetitionGateway, ArcadeCompetitionSnapshot } from './arcadeCompetitionApi'

const snapshot: ArcadeCompetitionSnapshot = {
  gameId: 'horse-flight',
  period: 'all-time',
  leaderboard: Array.from({ length: 12 }, (_, index) => ({
    position: index + 1,
    playerId: `rider-${index + 1}`,
    score: 1_000 - index,
  })),
}

describe('arcade competition leaderboard pagination', () => {
  let host: HTMLDivElement
  let root: Root

  beforeEach(() => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
    host = document.createElement('div')
    document.body.append(host)
    root = createRoot(host)
  })

  afterEach(async () => {
    await act(async () => root.unmount())
    host.remove()
    vi.unstubAllGlobals()
  })

  it('shows one compact page at a time and preserves the original placements', async () => {
    const gateway: ArcadeCompetitionGateway = {
      getLeaderboard: vi.fn().mockResolvedValue(snapshot),
    }

    await act(async () => {
      root.render(<ArcadeCompetitionLeaderboard gameId="horse-flight" gateway={gateway} pageSize={5} />)
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(host.textContent).toContain('rider-1')
    expect(host.textContent).toContain('rider-5')
    expect(host.textContent).not.toContain('rider-6')
    expect(host.textContent).toContain('1 / 3')

    const next = host.querySelector<HTMLButtonElement>('[aria-label="Next leaderboard page"]')
    expect(next).not.toBeNull()
    await act(async () => next!.click())

    expect(host.textContent).not.toContain('rider-5')
    expect(host.textContent).toContain('rider-6')
    expect(host.textContent).toContain('rider-10')
    expect(host.textContent).not.toContain('rider-11')
    expect(host.textContent).toContain('#6')
    expect(host.textContent).toContain('2 / 3')
  })
})

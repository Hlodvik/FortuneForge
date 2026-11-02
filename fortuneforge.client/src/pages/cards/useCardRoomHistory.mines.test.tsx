// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
import type { CardRoomHistoryResult } from '../../games/cards/shared/cardRoomHistoryApi'

const state = vi.hoisted(() => ({ history: [] as CardRoomHistoryResult[], seen: vi.fn() }))
vi.mock('../../games/cards/shared/cardRoomHistoryApi', () => ({
  getCardRoomHistory: async () => state.history,
  markCardRoomResultSeen: state.seen,
}))
vi.mock('../../games/cards/blackjack/blackjackTableApi', () => ({ getBlackjackTableSession: async () => ({ kind: 'idle' }) }))
vi.mock('../../games/cards/texasHoldem/creditHoldemApi', () => ({ getCreditHoldemSession: async () => ({ kind: 'idle' }) }))
vi.mock('../../games/cards/solitaire/solitaireApi', () => ({ getSolitaireSession: async () => ({ kind: 'idle' }) }))
vi.mock('../../games/cards/threeCardPoker/threeCardPokerApi', () => ({ getThreeCardPokerSession: async () => ({ kind: 'idle' }) }))
import { useCardRoomHistory } from './useCardRoomHistory'

afterEach(() => { vi.unstubAllGlobals(); state.seen.mockReset() })

it.each([1.02, .93, 0])('labels a R%s Mines return honestly and acknowledges it without claiming funds', async returned => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const balanceChanged = vi.fn()
  state.history = [1, 2].map(index => ({
    resultId: `mines-${index}`, game: 'mines', mode: 'credit', matchId: `round-${index}`,
    startedAtUtc: `2026-10-25T12:00:0${index}Z`, completedAtUtc: `2026-10-25T12:01:0${index}Z`,
    unseen: true, requiresClaim: false, winningsCredits: returned, wagerCredits: 1, netCredits: returned - 1,
    score: null, moves: null, schemaVersion: 1,
  }))
  let history!: ReturnType<typeof useCardRoomHistory>
  function Harness() { history = useCardRoomHistory(balanceChanged); return null }
  const host = document.createElement('div'); document.body.append(host)
  const root = createRoot(host)
  try {
    await act(async () => { root.render(<Harness />) })
    await act(async () => { await Promise.resolve() })
    expect(history.loading).toBe(false)
    expect(history.error).toBeNull()
    expect(history.activities).toHaveLength(2)
    expect(history.activities[0]).toMatchObject({ gameLabel: 'Mines', title: 'Mines result',
      summary: returned > 0 ? `R${returned.toFixed(2)} returned` : 'No return', rounds: 1,
      wagerCredits: 1, winningsCredits: returned, netCredits: returned - 1 })
    await act(async () => { await history.select(history.activities[0]) })
    expect(state.seen).toHaveBeenCalledExactlyOnceWith('mines-2')
    expect(history.activities[0].unseen).toBe(false)
    expect(balanceChanged).not.toHaveBeenCalled()
  } finally { act(() => root.unmount()); host.remove() }
})

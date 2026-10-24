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

it('maps each Andar Bahar settlement to its own named result and acknowledges it without claiming money', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const balanceChanged = vi.fn()
  state.history = [1, 2].map(index => ({
    resultId: `andar-bahar-${index}`, game: 'andar-bahar', mode: 'credit', matchId: `round-${index}`,
    startedAtUtc: `2026-10-24T12:00:0${index}Z`, completedAtUtc: `2026-10-24T12:00:0${index}Z`,
    unseen: true, requiresClaim: false, winningsCredits: .95, wagerCredits: .5, netCredits: .45,
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
    expect(history.activities[0]).toMatchObject({ gameLabel: 'Andar Bahar', title: 'Andar Bahar result', summary: 'R0.95 won', rounds: 1, wagerCredits: .5, winningsCredits: .95, netCredits: .45 })
    await act(async () => { await history.select(history.activities[0]) })
    expect(state.seen).toHaveBeenCalledExactlyOnceWith('andar-bahar-2')
    expect(history.activities[0].unseen).toBe(false)
    expect(balanceChanged).not.toHaveBeenCalled()
  } finally { act(() => root.unmount()); host.remove() }
})

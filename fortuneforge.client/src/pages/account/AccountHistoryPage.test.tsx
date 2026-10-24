// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, expect, it, vi } from 'vitest'
const state = vi.hoisted(() => ({
  select: vi.fn(),
  slots: [{ spinId: 'slot-1', result: 'win', wageredSlotsCredits: 1, wonSlotsCredits: 2, netSlotsCredits: 1, createdAtUtc: '2026-10-23T12:00:00Z' }],
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: { userId: 'player', playerName: 'Player', balances: { slotsCredits: 10 }, slots: { spinsPlayed: 1, wins: 1, losses: 0, netCredits: 1 } },
  error: null, isLoading: false,
}) }))
vi.mock('../../features/account/services/accountsApi', () => ({ getSlotHistory: async () => ({ spins: state.slots }) }))
vi.mock('../cards/useCardRoomHistory', () => ({ useCardRoomHistory: () => ({
  activities: [{ id: 'hi-lo-1', matchId: 'round-1', game: 'hi-lo', gameLabel: 'Hi-Lo', title: 'Hi-Lo game', summary: 'R2.14 won', startedAtUtc: '2026-10-23T12:00:00Z', completedAtUtc: '2026-10-23T12:01:00Z', unseen: true, requiresClaim: false, winningsCredits: 2.14 }],
  loading: false, error: null, busyId: null, select: state.select,
}) }))
import { AccountHistoryPage } from './AccountHistoryPage'
afterEach(() => vi.unstubAllGlobals())

it('makes settled Hi-Lo reachable from account history without replacing existing slot results', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const host = document.createElement('div'); document.body.append(host)
  const root = createRoot(host)
  try {
    await act(async () => { root.render(<AccountHistoryPage />); await Promise.resolve() })
    expect(host.querySelector('[aria-label="Card-game history"]')?.textContent).toContain('Hi-Lo game')
    expect(host.textContent).toContain('Spin results')
    expect(host.querySelectorAll('.history-row')).toHaveLength(1)
    const result = [...host.querySelectorAll('button')].find(button => button.textContent?.includes('Hi-Lo game'))!
    act(() => result.click())
    expect(state.select).toHaveBeenCalledOnce()
  } finally { act(() => root.unmount()); host.remove() }
})

// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { HeartsParticipation } from '@fortuneforge/games-hearts'
import type { AccountSummary } from '../../features/account/services/accountsApi'

const state = vi.hoisted(() => ({ guard: vi.fn(), callbacks: [] as ((value: HeartsParticipation) => void)[], userId: 'first' }))
vi.mock('../../features/navigation/GameLeaveGuard', () => ({ useGameLeaveGuard: state.guard }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({
  account: { ...account, userId: state.userId }, error: null, isLoading: false, reload: vi.fn(),
}) }))
vi.mock('@fortuneforge/games-hearts', () => ({ HttpHeartsGateway: class {},
  HeartsGame: ({ onParticipationChange }: { onParticipationChange: (value: HeartsParticipation) => void }) => {
    state.callbacks.push(onParticipationChange); return <div>Hearts game</div>
  },
}))
import { AuthenticatedHeartsRoute } from './HeartsRoute'

const account: AccountSummary = { userId: 'first', playerName: 'Player', email: 'player@example.test', createdAtUtc: '', role: 'player',
  balances: { slotsCredits: 100, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } }
let host: HTMLDivElement, root: Root
const guard = () => state.guard.mock.calls.at(-1)?.[0] as { title: string; description: string } | null
const render = () => act(() => root.render(<AuthenticatedHeartsRoute />))
const publish = (participation: HeartsParticipation) => act(() => state.callbacks.at(-1)!(participation))
beforeEach(() => { vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); state.userId = 'first'; state.guard.mockClear(); state.callbacks.length = 0;
  host = document.createElement('div'); document.body.append(host); root = createRoot(host) })
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })

describe('Hearts host explicit participation boundary', () => {
  it('stays unguarded until the package publishes participation and clears at completion', () => {
    render(); expect(guard()).toBeNull()
    publish('paid-match'); expect(guard()?.description).toContain('Missing a turn forfeits your entry')
    publish(null); expect(guard()).toBeNull()
  })
  it.each([
    ['paid-pending', 'may have been accepted'], ['paid-queue', 'does not cancel the queue'],
    ['free-match', 'No account credits are at stake'], ['trial', 'No account credits are at stake'],
    ['free-pending', 'still being opened'], ['trial-pending', 'still being opened'],
  ] as const)('uses accurate consequences for %s', (participation, expected) => {
    render(); publish(participation); expect(guard()?.description).toContain(expected)
  })
  it('does not allow an old account callback to guard a different account', () => {
    render(); const old = state.callbacks.at(-1)!
    publish('paid-match')
    state.userId = 'second'; render(); expect(guard()).toBeNull()
    act(() => old('paid-queue')); expect(guard()).toBeNull()
    publish('free-match'); expect(guard()?.description).toContain('practice progress')
  })
})

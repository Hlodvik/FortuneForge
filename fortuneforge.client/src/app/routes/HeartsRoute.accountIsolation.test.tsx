// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'

const state = vi.hoisted(() => ({ account: null as AccountSummary | null, balanceCallbacks: [] as ((value: number) => void)[] }))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({ useAuthenticatedAccount: () => ({ account: state.account, error: null, isLoading: false, reload: vi.fn() }) }))
vi.mock('../../components/InGameShell', () => ({ InGameShell: ({ account, children }: { account: AccountSummary; children: ReactNode }) => <><output data-testid="account-wallet">{account.userId}:{account.balances.slotsCredits}</output>{children}</> }))
vi.mock('@fortuneforge/games-hearts', () => ({
  HttpHeartsGateway: class {},
  HeartsGame: ({ onBalanceChange }: { onBalanceChange: (value: number) => void }) => { state.balanceCallbacks.push(onBalanceChange); return <div>Hearts game</div> },
}))
import { AuthenticatedHeartsRoute } from './HeartsRoute'

const account = (userId: string, credits: number): AccountSummary => ({ userId, playerName: userId, email: `${userId}@example.test`, createdAtUtc: '2026-10-21T00:00:00Z', balances: { slotsCredits: credits, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 }, role: 'player' })
let host: HTMLDivElement
let root: Root
beforeEach(() => { vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); host = document.createElement('div'); document.body.append(host); root = createRoot(host) })
afterEach(() => { act(() => root.unmount()); host.remove(); state.account = null; state.balanceCallbacks.length = 0; vi.unstubAllGlobals() })
const wallet = () => host.querySelector('[data-testid="account-wallet"]')?.textContent
const renderRoute = () => act(() => root.render(<AuthenticatedHeartsRoute />))

describe('Hearts route account isolation', () => {
  it('ignores a late old-game balance callback after switching authenticated accounts', () => {
    state.account = account('first', 100)
    renderRoute()
    const oldResponse = state.balanceCallbacks.at(-1)!
    expect(wallet()).toBe('first:100')

    state.account = account('second', 700)
    renderRoute()
    expect(wallet()).toBe('second:700')
    act(() => oldResponse(12))
    expect(wallet()).toBe('second:700')
    act(() => state.balanceCallbacks.at(-1)!(704))
    expect(wallet()).toBe('second:704')
  })

  it('still accepts refreshed account balances for the same user', () => {
    state.account = account('same', 100)
    renderRoute()
    state.account = account('same', 120)
    renderRoute()
    expect(wallet()).toBe('same:120')
  })
})

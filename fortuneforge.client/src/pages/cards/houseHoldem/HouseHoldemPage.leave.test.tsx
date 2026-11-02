// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import type { HouseHoldemRound, HouseHoldemStatus } from '../../../games/cards/houseHoldem/houseHoldemApi'
import { GameLeaveGuardProvider } from '../../../features/navigation/GameLeaveGuard'
import { requestGameNavigation } from '../../../features/navigation/gameLeaveNavigation'

const api = vi.hoisted(() => ({ status: vi.fn(), round: vi.fn(), start: vi.fn(), act: vi.fn(), advance: vi.fn() }))
vi.mock('../../../games/cards/houseHoldem/houseHoldemApi', async original => ({
  ...await original<typeof import('../../../games/cards/houseHoldem/houseHoldemApi')>(),
  getHouseHoldemStatus: api.status, getHouseHoldemRound: api.round, startHouseHoldemRound: api.start,
  actHouseHoldemRound: api.act, advanceHouseHoldemRound: api.advance,
}))
vi.mock('../../../components/InGameShell', () => ({ InGameShell: ({ children }: { children: ReactNode }) => <>{children}</> }))
vi.mock('../../../games/cards/shared/cardAudio', () => ({ playCardAudio: vi.fn(), useCardAudioClick: () => () => {} }))
import { HouseHoldemPage } from './HouseHoldemPage'

const account = { userId: 'leave-test', balances: { slotsCredits: 100, freeGames: 0 } } as AccountSummary
const status: HouseHoldemStatus = { available: true, balance: 100, activeRoundId: null, minimumAnte: 1, maximumAnte: 100, wagerIncrement: 1, maximumTrips: 500 }
const initial: HouseHoldemRound = { roundId: 'saved-hand', balance: 98, phase: 'preflop', ante: 1, blind: 1, trips: 0, play: 0,
  totalWager: 2, payout: 0, playerCards: [{ rank: 'ace', suit: 'spades' }, { rank: 'king', suit: 'hearts' }],
  dealerCards: null, board: [], playerHand: null, dealerHand: null, result: null, allowedActions: [], canAdvance: false, payoutBreakdown: null }
let root: Root, host: HTMLDivElement
const navigation = vi.fn()
function unloading() { const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented }
async function mount() { await act(async () => root.render(<GameLeaveGuardProvider><HouseHoldemPage account={account} /></GameLeaveGuardProvider>)) }
async function leave() { await act(async () => requestGameNavigation(navigation)) }
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState(null, '', '/cards/house-holdem'); sessionStorage.clear(); navigation.mockReset()
  for (const mock of Object.values(api)) mock.mockReset()
  api.status.mockResolvedValue(status); api.round.mockResolvedValue(initial)
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); sessionStorage.clear(); vi.unstubAllGlobals() })

describe('House Hold’em accepted-hand leave lifecycle', () => {
  it.each(['preflop', 'flop', 'river'] as const)('protects the whole %s hand with no current decision, including after Play', async phase => {
    api.status.mockResolvedValue({ ...status, activeRoundId: initial.roundId })
    api.round.mockResolvedValue({ ...initial, phase, play: phase === 'preflop' ? 0 : 3, canAdvance: true })
    await mount(); expect(unloading()).toBe(true); await leave()
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('hand stays open')
    await act(async () => [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(button => button.textContent === 'Leave page')!.click())
    expect(navigation).toHaveBeenCalledOnce()
    expect(api.act).not.toHaveBeenCalled(); expect(api.advance).not.toHaveBeenCalled(); expect(api.start).not.toHaveBeenCalled()
  })

  it('does not guard idle local chips before Deal is submitted', async () => {
    await mount()
    await act(async () => host.querySelector<HTMLButtonElement>('[aria-label="Place chip on Ante"]')!.click())
    expect(unloading()).toBe(false); await leave(); expect(navigation).toHaveBeenCalledOnce()
    expect(document.querySelector('[role="dialog"]')).toBeNull(); expect(api.start).not.toHaveBeenCalled()
  })

  it('clears protection when a restored hand is known to be settled', async () => {
    sessionStorage.setItem('fortune-forge.house-holdem.round.leave-test', initial.roundId)
    api.round.mockResolvedValue({ ...initial, phase: 'settled', result: 'dealer-win', payoutBreakdown: { ante: 0, blind: 0, trips: 0, play: 0 } })
    await mount(); expect(unloading()).toBe(false); await leave()
    expect(navigation).toHaveBeenCalledOnce(); expect(document.querySelector('[role="dialog"]')).toBeNull()
  })

  it('retains a saved accepted-hand warning while recovery is offline', async () => {
    sessionStorage.setItem('fortune-forge.house-holdem.round.leave-test', initial.roundId)
    api.status.mockRejectedValue(new Error('offline'))
    await mount(); expect(unloading()).toBe(true); await leave()
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('not been reconciled')
    expect(api.act).not.toHaveBeenCalled(); expect(api.start).not.toHaveBeenCalled()
  })

  it('keeps uncertain Deal protected and never uses navigation or backgrounding to cancel it', async () => {
    await mount()
    api.start.mockRejectedValue(new Error('response lost'))
    await act(async () => host.querySelector<HTMLButtonElement>('[aria-label="Place chip on Ante"]')!.click())
    await act(async () => host.querySelector<HTMLButtonElement>('[aria-label="Deal"]')!.click())
    expect(api.start).toHaveBeenCalledOnce(); expect(unloading()).toBe(true)
    document.dispatchEvent(new Event('visibilitychange')); window.dispatchEvent(new Event('pagehide'))
    await leave()
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('may already have been accepted')
    expect(navigation).not.toHaveBeenCalled(); expect(api.start).toHaveBeenCalledOnce()
    expect(api.act).not.toHaveBeenCalled(); expect(api.advance).not.toHaveBeenCalled()
  })
})

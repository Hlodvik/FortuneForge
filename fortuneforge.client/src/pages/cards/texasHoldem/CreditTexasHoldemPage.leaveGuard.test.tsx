// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import type { CreditHoldemSession, CreditHoldemTable } from '../../../games/cards/texasHoldem/creditHoldemApi'

const api = vi.hoisted(() => ({ status: vi.fn(), session: vi.fn(), join: vi.fn(), cancel: vi.fn(), leave: vi.fn(), guard: vi.fn() }))
vi.mock('../../../features/navigation/GameLeaveGuard', () => ({ useGameLeaveGuard: api.guard }))
vi.mock('../../../games/cards/shared/cardAudio', () => ({ useCardAudioClick: () => vi.fn() }))
vi.mock('../../../features/account/services/accountsApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../../features/account/services/accountsApi')>(), getCurrentAccount: vi.fn(async () => account),
}))
vi.mock('../../../games/cards/texasHoldem/creditHoldemApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../../games/cards/texasHoldem/creditHoldemApi')>(),
  getCreditHoldemStatus: api.status, getCreditHoldemSession: api.session,
  joinCreditHoldemQueue: api.join, cancelCreditHoldemQueue: api.cancel, leaveCreditHoldemTable: api.leave,
}))
import { CreditTexasHoldemPage } from './CreditTexasHoldemPage'
import { CreditHoldemRequestError } from '../../../games/cards/texasHoldem/creditHoldemApi'

const account: AccountSummary = {
  userId: 'leave-test', playerName: 'Player', email: 'player@example.test', createdAtUtc: '', role: 'player',
  balances: { slotsCredits: 100, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}
const status = { available: true, minimumStartPlayers: 3, maximumSeats: 5, minimumRealPlayers: 2,
  smallBlindCredits: .5, bigBlindCredits: 1, actionDeadlineSeconds: 30, matchDeadlineSeconds: 300 }
const idle = { contractVersion: 'cards.texas-holdem.credit.v2', kind: 'idle', version: 1 } as const
const queue: CreditHoldemSession = { ...idle, kind: 'queue', version: 2, ticketId: 'accepted-reservation',
  position: 1, joinedAtUtc: '', humanGraceEndsAtUtc: '', players: [] }
const table: CreditHoldemTable = {
  matchId: 'accepted-hand', status: 'active', street: 'pre-flop', handNumber: 2, dealerSeat: 1, activeSeat: 1,
  pot: 100, currentBet: 100, minimumRaiseTo: 200, maximumRaiseTo: 3250, communityCards: [], legalActions: [],
  winningSeatIds: [], winningAmount: 0, startedAtUtc: '', matchDeadlineAtUtc: '', remainingActionMilliseconds: 30000,
  seats: [{ seatId: 'viewer', displayName: 'Player', seat: 0, startingStack: 3250, stack: 3250,
    committed: 100, committedRound: 100, status: 'active', holeCards: [{ hidden: true }, { hidden: true }], isCurrentPlayer: true }],
}
let root: Root, host: HTMLDivElement
const guard = () => api.guard.mock.calls.at(-1)?.[0] as { title: string; description: string } | null
const open = () => act(async () => root.render(<CreditTexasHoldemPage account={account} />))
const click = async (label: RegExp) => {
  const button = [...host.querySelectorAll<HTMLButtonElement>('button')].find(button => label.test(button.textContent ?? ''))!
  expect(button).toBeTruthy()
  await act(async () => button.click())
}
beforeEach(() => {
  vi.useFakeTimers()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  Object.values(api).forEach(mock => mock.mockReset())
  api.status.mockResolvedValue(status); api.session.mockResolvedValue(idle)
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('Texas Hold’em accepted participation leave guard', () => {
  it('leaves an unaccepted lobby unguarded', async () => {
    await open()
    expect(guard()).toBeNull()
  })

  it.each(['active', 'folded', 'all-in'])('protects the entire accepted hand while the viewer is %s and waiting', async seatStatus => {
    api.session.mockResolvedValue({ ...idle, kind: 'match', table: { ...table,
      seats: table.seats.map(seat => ({ ...seat, status: seatStatus })) } })
    await open()
    expect(guard()?.description).toContain('turn timer continues')
    expect(api.leave).not.toHaveBeenCalled()
    expect(api.cancel).not.toHaveBeenCalled()
  })

  it('protects a reserved queue without promising navigation will refund it', async () => {
    api.session.mockResolvedValue(queue)
    await open()
    expect(guard()?.description).toContain('buy-in is reserved')
    expect(guard()?.description).toContain('does not cancel the queue')
    expect(api.cancel).not.toHaveBeenCalled()
  })

  it('keeps an uncertain join protected through an idle refresh and clears after a definite rejection', async () => {
    api.join.mockRejectedValueOnce(new TypeError('Response lost'))
      .mockRejectedValueOnce(new CreditHoldemRequestError('Rejected', 400, { code: 'insufficient-credits' }))
    await open()
    await click(/^Join /)
    expect(guard()?.description).toContain('may have been accepted')
    await click(/^Refresh table$/)
    expect(guard()?.description).toContain('may have been accepted')
    await click(/^Retry request$/)
    expect(guard()).toBeNull()
    expect(api.leave).not.toHaveBeenCalled()
    expect(api.cancel).not.toHaveBeenCalled()
  })

  it('retains the last confirmed match through a failed server read', async () => {
    api.session.mockResolvedValueOnce({ ...idle, kind: 'match', table })
      .mockRejectedValue(new CreditHoldemRequestError('Disabled', 503, { code: 'credit-texas-holdem-disabled' }))
    await open()
    await act(async () => { await vi.advanceTimersByTimeAsync(950) })
    expect(host.textContent).toContain('Credit Hold’em is coming soon')
    expect(guard()?.description).toContain('turn timer continues')
    expect(api.leave).not.toHaveBeenCalled()
  })

  it('releases the guard on an authoritative completed result', async () => {
    api.session.mockResolvedValueOnce({ ...idle, kind: 'match', table })
      .mockResolvedValue({ ...idle, kind: 'result', matchId: table.matchId, handNumber: 2,
        humanCommittedCredits: 1, humanPayoutCredits: 0, houseNetCredits: 1, startedAtUtc: '', completedAtUtc: '',
        standings: [], finalTable: { ...table, status: 'completed', activeSeat: -1 } })
    await open()
    expect(guard()).not.toBeNull()
    await act(async () => { await vi.advanceTimersByTimeAsync(950) })
    expect(guard()).toBeNull()
  })
})

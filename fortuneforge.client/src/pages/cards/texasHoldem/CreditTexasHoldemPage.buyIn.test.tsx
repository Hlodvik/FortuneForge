// @vitest-environment jsdom
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { fireEvent } from '@testing-library/dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import type { CreditHoldemSession, CreditHoldemTable } from '../../../games/cards/texasHoldem/creditHoldemApi'

const api = vi.hoisted(() => ({ status: vi.fn(), session: vi.fn(), join: vi.fn(), cancel: vi.fn(), next: vi.fn(), leave: vi.fn(), account: vi.fn() }))
vi.mock('../../../games/cards/shared/cardAudio', () => ({ useCardAudioClick: () => vi.fn() }))
vi.mock('../../../components/InGameShell', () => ({
  InGameShell: ({ children, account }: { children: ReactNode; account: AccountSummary }) => <div data-account-balance={account.balances.slotsCredits}>{children}</div>,
}))
vi.mock('../../../features/account/services/accountsApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../../features/account/services/accountsApi')>(),
  getCurrentAccount: (...args: unknown[]) => api.account(...args),
}))
vi.mock('../../../games/cards/texasHoldem/creditHoldemApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../../games/cards/texasHoldem/creditHoldemApi')>(),
  getCreditHoldemStatus: (...args: unknown[]) => api.status(...args),
  getCreditHoldemSession: (...args: unknown[]) => api.session(...args),
  joinCreditHoldemQueue: (...args: unknown[]) => api.join(...args),
  cancelCreditHoldemQueue: (...args: unknown[]) => api.cancel(...args),
  dealNextCreditHoldemHand: (...args: unknown[]) => api.next(...args),
  leaveCreditHoldemTable: (...args: unknown[]) => api.leave(...args),
}))

import { CreditTexasHoldemPage } from './CreditTexasHoldemPage'
import { CreditHoldemRequestError } from '../../../games/cards/texasHoldem/creditHoldemApi'

let host: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.status.mockResolvedValue(status)
  api.session.mockResolvedValue(idle)
  api.account.mockResolvedValue(account)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.useRealTimers(); vi.unstubAllGlobals() })

const button = (text: string) => {
  const match = [...host.querySelectorAll<HTMLButtonElement>('button')].find(element => element.textContent === text)
  expect(match, text).toBeTruthy()
  return match!
}
const balance = () => Number(host.querySelector('[data-account-balance]')?.getAttribute('data-account-balance'))
async function open() { await act(async () => root.render(<CreditTexasHoldemPage account={account}/>)) }
async function setBuyIn(value: string) {
  const input = host.querySelector<HTMLInputElement>('.ff-holdem-buy-in__amount input')!
  expect(input).toBeTruthy()
  act(() => fireEvent.change(input, { target: { value } }))
}
async function click(text: string) { await act(async () => button(text).click()) }

describe('Hold’em chosen buy-in reservations', () => {
  it('sends the selected amount, shows the server wallet after reservation, and refunds through queue cancellation', async () => {
    api.join.mockResolvedValue({ session: queue, balanceCredits: 87.65 })
    api.cancel.mockResolvedValue({ session: { ...idle, version: 3 }, balanceCredits: 100 })
    await open()
    expect(button('Join Standard · R100.00')).toBeTruthy()
    await setBuyIn('12.35')
    await click('Join Standard · R12.35')
    expect(api.join).toHaveBeenCalledWith(1, expect.any(String), 'standard', 12.35)
    expect(balance()).toBe(87.65)
    expect(host.textContent).toContain('Finding table…')
    expect(host.textContent).not.toContain('Queue position')
    await click('Cancel')
    expect(api.cancel).toHaveBeenCalledWith('ticket-1', 2, expect.any(String))
    expect(balance()).toBe(100)
    expect(button('Join Standard · R12.35')).toBeTruthy()
  })

  it('freezes an uncertain join and retries the identical payload and key even after an idle refresh', async () => {
    api.join.mockRejectedValueOnce(new TypeError('Connection interrupted')).mockResolvedValueOnce({ session: queue, balanceCredits: 87.65 })
    await open()
    await setBuyIn('12.35')
    await click('Join Standard · R12.35')
    const first = api.join.mock.calls[0]
    expect(host.querySelector('.ff-holdem-buy-in')).toBeNull()
    expect(host.textContent).toContain('Finding table…')
    expect(balance()).toBe(100)
    await click('Refresh table')
    expect(button('Retry request')).toBeTruthy()
    await click('Retry request')
    expect(api.join).toHaveBeenCalledTimes(2)
    expect(api.join.mock.calls[1]).toEqual(first)
    expect(balance()).toBe(87.65)
  })

  it('recovers a committed join via session without submitting again and refreshes the real wallet once', async () => {
    api.join.mockRejectedValue(new TypeError('Connection interrupted'))
    await open()
    await setBuyIn('20')
    await click('Join Standard · R20.00')
    api.session.mockResolvedValue(queue)
    api.account.mockResolvedValue({ ...account, balances: { ...account.balances, slotsCredits: 80 } })
    await click('Refresh table')
    expect(api.join).toHaveBeenCalledOnce()
    expect(api.account).toHaveBeenCalledOnce()
    expect(balance()).toBe(80)
    expect(host.textContent).not.toContain('Retry request')
    expect(button('Cancel')).toBeTruthy()
  })

  it('returns to the retained amount after a definite rejection and gives a changed amount its own key', async () => {
    api.join.mockRejectedValueOnce(new CreditHoldemRequestError('Not enough credits.', 400, { code: 'insufficient-credits' }))
      .mockResolvedValueOnce({ session: queue, balanceCredits: 80 })
    await open()
    await setBuyIn('12.35')
    await click('Join Standard · R12.35')
    expect(button('Join Standard · R12.35')).toBeTruthy()
    expect(host.textContent).not.toContain('Retry request')
    await setBuyIn('20')
    await click('Join Standard · R20.00')
    expect(api.join.mock.calls[1][1]).not.toBe(api.join.mock.calls[0][1])
    expect(api.join.mock.calls[1][3]).toBe(20)
  })

  it('starts the next hand without a new buy-in or wallet refill and uses the leave response for cash-out', async () => {
    api.session.mockResolvedValue(result)
    api.account.mockResolvedValue({ ...account, balances: { ...account.balances, slotsCredits: 75 } })
    api.next.mockResolvedValue({ session: { contractVersion: idle.contractVersion, kind: 'match', version: 5, table }, balanceCredits: 75 })
    api.leave.mockResolvedValue({ session: { ...idle, version: 6 }, balanceCredits: 107.5 })
    await open()
    expect(balance()).toBe(75)
    await click('Next hand')
    expect(api.next).toHaveBeenCalledWith('match-1', 4, expect.any(String))
    expect(api.join).not.toHaveBeenCalled()
    expect(balance()).toBe(75)
    await click('Leave table')
    const confirm = host.querySelector('[role="alertdialog"]')!
    await act(async () => [...confirm.querySelectorAll<HTMLButtonElement>('button')].find(element => element.textContent === 'Leave table')!.click())
    expect(api.leave).toHaveBeenCalledWith('match-1', 5, expect.any(String))
    expect(balance()).toBe(107.5)
  })

  it.each([
    ['queue expiry', queue, 950, 87.65, 100],
    ['completed table expiry', result, 4200, 75, 107.5],
  ] as const)('refreshes the wallet once when GET session returns idle after %s', async (_name, previous, interval, reserved, refunded) => {
    vi.useFakeTimers()
    api.session.mockResolvedValueOnce(previous).mockResolvedValue({ ...idle, version: 9 })
    api.account.mockResolvedValueOnce({ ...account, balances: { ...account.balances, slotsCredits: reserved } })
      .mockResolvedValue({ ...account, balances: { ...account.balances, slotsCredits: refunded } })
    await open()
    expect(balance()).toBe(reserved)
    await act(async () => { vi.advanceTimersByTime(interval) })
    expect(balance()).toBe(refunded)
    expect(host.querySelector('.ff-holdem-lobby')).toBeTruthy()
    expect(api.account).toHaveBeenCalledTimes(2)
    await act(async () => { vi.advanceTimersByTime(4200) })
    expect(api.account).toHaveBeenCalledTimes(2)
    expect(api.cancel).not.toHaveBeenCalled()
    expect(api.leave).not.toHaveBeenCalled()
  })

  it('does not let an older wallet refresh overwrite a server-driven refund', async () => {
    vi.useFakeTimers()
    let completeOldRead!: (value: AccountSummary) => void
    api.session.mockResolvedValueOnce(queue).mockResolvedValue({ ...idle, version: 9 })
    api.account.mockImplementationOnce(() => new Promise<AccountSummary>(resolve => { completeOldRead = resolve }))
      .mockResolvedValue({ ...account, balances: { ...account.balances, slotsCredits: 100 } })
    await open()
    await act(async () => { vi.advanceTimersByTime(950) })
    expect(api.account).toHaveBeenCalledTimes(2)
    expect(balance()).toBe(100)
    await act(async () => { completeOldRead({ ...account, balances: { ...account.balances, slotsCredits: 87.65 } }) })
    expect(balance()).toBe(100)
  })
})

const rule = { id: 'standard', name: 'Standard', description: 'Automatic blinds · no ante', smallBlindCredits: .5, bigBlindCredits: 1, anteCredits: 0, maximumTableStackCredits: 100, minimumBuyInCredits: 1 }
const status = { available: true, minimumStartPlayers: 3, maximumSeats: 5, minimumRealPlayers: 2, smallBlindCredits: .5, bigBlindCredits: 1, actionDeadlineSeconds: 30, matchDeadlineSeconds: 300, tableRules: [rule] }
const idle = { contractVersion: 'cards.texas-holdem.credit.v2', kind: 'idle', version: 1 } as const
const queue: CreditHoldemSession = { ...idle, kind: 'queue', version: 2, ticketId: 'ticket-1', position: 1, joinedAtUtc: '', humanGraceEndsAtUtc: '', players: [], tableRule: rule }
const table: CreditHoldemTable = {
  matchId: 'match-1', status: 'active', street: 'pre-flop', handNumber: 2, dealerSeat: 1, activeSeat: 0,
  pot: 100, currentBet: 100, minimumRaiseTo: 200, maximumRaiseTo: 3250, communityCards: [], legalActions: ['fold', 'call', 'raise'],
  winningSeatIds: [], winningAmount: 0, startedAtUtc: '', matchDeadlineAtUtc: '', remainingActionMilliseconds: 30000,
  seats: [{ seatId: 'viewer', displayName: 'Player', seat: 0, startingStack: 3250, stack: 3250, committed: 0, committedRound: 0, status: 'active', holeCards: [{ hidden: true }, { hidden: true }], isCurrentPlayer: true }],
}
const result: CreditHoldemSession = {
  ...idle, kind: 'result', version: 4, matchId: 'match-1', handNumber: 1, humanCommittedCredits: 0, humanPayoutCredits: 0, houseNetCredits: 0, startedAtUtc: '', completedAtUtc: '',
  standings: [{ rank: 1, seatId: 'viewer', displayName: 'Player', finalStack: 3250, status: 'active', payoutCredits: 0, isCurrentPlayer: true }],
  finalTable: { ...table, status: 'completed', legalActions: [] },
}
const account: AccountSummary = {
  userId: 'buyin-test', playerName: 'Player', email: 'player@example.test', createdAtUtc: '', role: 'player', balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
}

// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SicBoGame } from './SicBoGame'
import { SicBoGatewayError, type SicBoGateway, type SicBoRound, type SicBoStatus } from './contracts'

const status: SicBoStatus = {
  available: true, minimumStake: 1, maximumStakePerBet: 25, stakeIncrement: 1,
  maximumBetsPerRound: 5, balance: 100, mode: 'test-table',
}
const settledRound: SicBoRound = {
  roundId: 'sic-11', balance: 107, phase: 'settled', dice: [2, 2, 5], total: 9, isTriple: false,
  totalStaked: 3, totalReturn: 10, profit: 7,
  settlements: [
    { betIndex: 0, kind: 'small', stake: 1, face: null, total: null, firstFace: null, secondFace: null, won: true, profitOdds: 1, profit: 1, totalReturn: 2 },
    { betIndex: 1, kind: 'total', stake: 1, face: null, total: 9, firstFace: null, secondFace: null, won: true, profitOdds: 6, profit: 6, totalReturn: 7 },
    { betIndex: 2, kind: 'two-number-combination', stake: 1, face: null, total: null, firstFace: 2, secondFace: 5, won: true, profitOdds: 1, profit: 0, totalReturn: 1 },
  ],
}

afterEach(() => { cleanup(); sessionStorage.clear() })

describe('SicBoGame', () => {
  it('retries an unavailable table connection without requiring a page refresh', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockRejectedValueOnce(new Error('Offline.')).mockResolvedValueOnce(status)
    render(<SicBoGame gateway={fakeGateway({ getStatus })} />)

    await user.click(await screen.findByRole('button', { name: 'Retry connection' }))

    expect(await screen.findByText('Table open')).toBeTruthy()
    expect(getStatus).toHaveBeenCalledTimes(2)
  })

  it('does not misrepresent a deliberately unavailable table as a connection failure', async () => {
    render(<SicBoGame gateway={fakeGateway({ getStatus: vi.fn().mockRejectedValue(new SicBoGatewayError('Not ready.', 'sic-bo-disabled', 503)) })} />)

    expect((await screen.findByRole('alert')).textContent).toContain('temporarily unavailable')
    expect(screen.queryByRole('button', { name: 'Retry connection' })).toBeNull()
  })

  it('loads service availability, balance, and table limits', async () => {
    render(<SicBoGame gateway={fakeGateway()} />)

    expect(screen.getByText('Loading Sic Bo table…')).toBeTruthy()
    expect(await screen.findByText('Table open')).toBeTruthy()
    expect(screen.getAllByText('R100.00').length).toBeGreaterThan(0)
    expect(screen.getByText('Max R25.00 per bet')).toBeTruthy()
    expect(screen.getByText('5 bets per round')).toBeTruthy()
  })

  it('makes the authoritative bet conditions and profit odds available before a roll', async () => {
    render(<SicBoGame gateway={fakeGateway()} />)

    await screen.findByText('Table open')

    expect(screen.getByText('11.5:1')).toBeTruthy()
    expect(screen.getByText('195:1')).toBeTruthy()
    expect(screen.getByText(/Winning bets return the stake plus the listed profit/)).toBeTruthy()
  })

  it('adds and removes varied bets, normalizing a two-number combination', async () => {
    const user = userEvent.setup()
    render(<SicBoGame gateway={fakeGateway()} />)
    await screen.findByText('Table open')

    await user.click(screen.getByRole('button', { name: /Small/ }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Bet type' }), 'two-number-combination')
    await user.selectOptions(screen.getByRole('combobox', { name: 'First face' }), '6')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Second face' }), '2')
    await user.click(screen.getByRole('button', { name: 'Add Bet' }))

    expect(screen.getByRole('button', { name: 'Remove Small' })).toBeTruthy()
    expect(screen.getByText('Combination 2 + 6')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Remove Small' }))
    expect(screen.queryByRole('button', { name: 'Remove Small' })).toBeNull()
    expect(screen.getByText('1 / 5 bets')).toBeTruthy()
  })

  it('posts the visible bet slip once and in exact order', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway()
    render(<SicBoGame gateway={gateway} />)
    await screen.findByText('Table open')

    await user.click(screen.getByRole('button', { name: /Small/ }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Bet type' }), 'total')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Total' }), '9')
    await user.click(screen.getByRole('button', { name: 'Add Bet' }))
    await user.click(screen.getByRole('button', { name: 'Roll Dice' }))

    expect(gateway.createRound).toHaveBeenCalledTimes(1)
    expect(gateway.createRound).toHaveBeenCalledWith([
      { kind: 'small', stake: 1, face: null, total: null, firstFace: null, secondFace: null },
      { kind: 'total', stake: 1, face: null, total: 9, firstFace: null, secondFace: null },
    ], expect.objectContaining({ idempotencyKey: expect.stringMatching(/^sic-bo-/) }))
  })

  it('renders CSS dice, ordered settlements, and returned totals', async () => {
    const user = userEvent.setup()
    render(<SicBoGame gateway={fakeGateway({ createRound: vi.fn().mockResolvedValue(settledRound) })} />)
    await screen.findByText('Table open')
    await user.click(screen.getByRole('button', { name: /Small/ }))
    await user.click(screen.getByRole('button', { name: 'Roll Dice' }))

    expect(await screen.findByRole('heading', { name: 'Total 9' })).toBeTruthy()
    expect(screen.getByLabelText('Dice: 2, 2, 5')).toBeTruthy()
    expect(screen.getByText('Combination 2 + 5')).toBeTruthy()
    expect(screen.getByText('+R7.00')).toBeTruthy()
    expect(screen.getAllByText('R107.00').length).toBeGreaterThan(0)
  })

  it('prevents duplicate requests and editing while rolling', async () => {
    const user = userEvent.setup()
    let resolveRound: ((round: SicBoRound) => void) | undefined
    const pending = new Promise<SicBoRound>(resolve => { resolveRound = resolve })
    const gateway = fakeGateway({ createRound: vi.fn().mockReturnValue(pending) })
    render(<SicBoGame gateway={gateway} />)
    await screen.findByText('Table open')
    await user.click(screen.getByRole('button', { name: /Small/ }))
    await user.click(screen.getByRole('button', { name: 'Roll Dice' }))

    expect((screen.getByRole('button', { name: 'Rolling…' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: /Add Bet/ }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Rolling…' }))
    expect(gateway.createRound).toHaveBeenCalledTimes(1)

    resolveRound?.(settledRound)
    await screen.findByRole('button', { name: 'New Round' })
  })

  it('surfaces errors and allows a recovered roll', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockRejectedValueOnce(new SicBoGatewayError('Table paused.', 'paused', 503)).mockResolvedValueOnce(settledRound) })
    render(<SicBoGame gateway={gateway} />)
    await screen.findByText('Table open')
    await user.click(screen.getByRole('button', { name: /Small/ }))
    await user.click(screen.getByRole('button', { name: 'Roll Dice' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Table paused.')
    expect((screen.getByRole('button', { name: /Add Bet/ }) as HTMLButtonElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Retry Roll' }))
    expect(await screen.findByRole('button', { name: 'New Round' })).toBeTruthy()
    const calls = vi.mocked(gateway.createRound).mock.calls
    expect(calls[1]?.[1]?.idempotencyKey).toBe(calls[0]?.[1]?.idempotencyKey)
  })

  it('replays the exact player-scoped pending slip after a reload', async () => {
    const bets = [
      { kind: 'small', stake: 1, face: null, total: null, firstFace: null, secondFace: null },
      { kind: 'total', stake: 1, face: null, total: 9, firstFace: null, secondFace: null },
    ]
    sessionStorage.setItem('fortuneforge:sic-bo:pending:player-7', JSON.stringify({ idempotencyKey: 'sic-bo-recovery-0001', bets }))
    const createRound = vi.fn().mockResolvedValue(settledRound)
    render(<SicBoGame playerId="player-7" gateway={fakeGateway({ createRound })} />)

    expect(await screen.findByRole('button', { name: 'New Round' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledWith(bets, expect.objectContaining({
      idempotencyKey: 'sic-bo-recovery-0001', signal: expect.any(AbortSignal),
    }))
  })

  it('clears the settled result and slip without refreshing status', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue(settledRound) })
    render(<SicBoGame gateway={gateway} />)
    await screen.findByText('Table open')
    await user.click(screen.getByRole('button', { name: /Small/ }))
    await user.click(screen.getByRole('button', { name: 'Roll Dice' }))
    await user.click(await screen.findByRole('button', { name: 'New Round' }))

    expect(screen.getByText('Your bet slip is empty.')).toBeTruthy()
    expect(screen.getAllByText('R107.00').length).toBeGreaterThan(0)
    expect(gateway.getStatus).toHaveBeenCalledTimes(1)
  })

  it('disables additions at both the bet-count and balance limits', async () => {
    const user = userEvent.setup()
    const limited = { ...status, minimumStake: 5, maximumStakePerBet: 5, maximumBetsPerRound: 1, balance: 5 }
    render(<SicBoGame gateway={fakeGateway({ getStatus: vi.fn().mockResolvedValue(limited) })} />)
    await screen.findByText('Table open')
    await user.click(screen.getByRole('button', { name: /Small/ }))

    expect((screen.getByRole('button', { name: /Big/ }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Add Bet' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('R0.00')).toBeTruthy()
  })
})

function fakeGateway(overrides: Partial<SicBoGateway> = {}): SicBoGateway {
  return { getStatus: vi.fn().mockResolvedValue(status), createRound: vi.fn().mockResolvedValue(settledRound), ...overrides }
}

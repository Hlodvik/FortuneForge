// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BaccaratGame } from './BaccaratGame'
import { BaccaratGatewayError, type BaccaratGateway, type BaccaratRound, type BaccaratStatus } from './contracts'

const status: BaccaratStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, balance: 1_000, mode: 'test' }
const settledRound: BaccaratRound = {
  roundId: 'round-11', balance: 1_009.5, betSide: 'banker', stake: 10, phase: 'settled',
  playerCards: [{ rank: 'eight', suit: 'spades' }, { rank: 'eight', suit: 'spades' }],
  bankerCards: [{ rank: 'nine', suit: 'clubs' }, { rank: 'king', suit: 'diamonds' }],
  playerTotal: 6, bankerTotal: 9, outcome: 'banker', endedOnNatural: true,
  disposition: 'win', profit: 9.5, totalReturn: 19.5,
}

afterEach(() => { cleanup(); sessionStorage.clear() })

describe('BaccaratGame', () => {
  it('retries an unavailable table connection without requiring a page refresh', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockRejectedValueOnce(new Error('Offline.')).mockResolvedValueOnce(status)
    render(<BaccaratGame gateway={fakeGateway({ getStatus })} />)

    const retry = await screen.findByRole('button', { name: 'Retry connection' })
    expect(retry.className).toContain('ff-baccarat__primary')
    await user.click(retry)

    expect(await screen.findByRole('button', { name: 'Deal' })).toBeTruthy()
    expect(getStatus).toHaveBeenCalledTimes(2)
  })

  it('does not misrepresent a deliberately unavailable table as a connection failure', async () => {
    render(<BaccaratGame gateway={fakeGateway({ getStatus: vi.fn().mockRejectedValue(new BaccaratGatewayError('Not ready.', 'baccarat-disabled', 503)) })} />)

    expect((await screen.findByRole('alert')).textContent).toContain('temporarily unavailable')
    expect(screen.queryByRole('button', { name: 'Retry connection' })).toBeNull()
  })

  it('submits the selected bet and stake and renders a settled multi-deck hand', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue(settledRound) })
    render(<BaccaratGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: /Banker/ }))
    const stake = screen.getByRole('spinbutton', { name: 'Stake' })
    await user.clear(stake)
    await user.type(stake, '10')
    await user.click(screen.getByRole('button', { name: 'Deal' }))

    expect(gateway.createRound).toHaveBeenCalledWith('banker', 10, expect.objectContaining({ idempotencyKey: expect.stringMatching(/^baccarat-/) }))
    expect(await screen.findByText('Banker wins')).toBeTruthy()
    expect(screen.getAllByLabelText('eight of spades')).toHaveLength(2)
    expect(screen.getByText('Natural')).toBeTruthy()
    expect(screen.getByText('+R9.50')).toBeTruthy()
  })

  it('prevents repeated deals while busy', async () => {
    const user = userEvent.setup()
    let resolveRound: ((round: BaccaratRound) => void) | undefined
    const pendingRound = new Promise<BaccaratRound>(resolve => { resolveRound = resolve })
    const gateway = fakeGateway({ createRound: vi.fn().mockReturnValue(pendingRound) })
    render(<BaccaratGame gateway={gateway} />)

    const deal = await screen.findByRole('button', { name: 'Deal' })
    await user.click(deal)
    expect((screen.getByRole('button', { name: 'Dealing…' }) as HTMLButtonElement).disabled).toBe(true)
    expect(gateway.createRound).toHaveBeenCalledTimes(1)

    resolveRound?.(settledRound)
    await screen.findByRole('button', { name: 'Deal Again' })
  })

  it('puts a losing profit sign before the Rand symbol', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue({ ...settledRound, disposition: 'loss', profit: -10, totalReturn: 0 }) })
    render(<BaccaratGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))

    expect(await screen.findByText('-R10.00')).toBeTruthy()
  })

  it('reuses the original deal key when a failed request is retried', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockRejectedValueOnce(new BaccaratGatewayError('Connection lost.')).mockResolvedValueOnce(settledRound)
    const gateway = fakeGateway({ createRound })
    render(<BaccaratGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    await screen.findByRole('alert')
    expect((screen.getByRole('spinbutton', { name: 'Stake' }) as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: /Banker/ }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Retry Deal' }))
    await screen.findByRole('button', { name: 'Deal Again' })

    expect(createRound.mock.calls[1]?.[2]?.idempotencyKey).toBe(createRound.mock.calls[0]?.[2]?.idempotencyKey)
  })

  it('replays the exact player-scoped pending deal after a reload', async () => {
    sessionStorage.setItem('fortuneforge:baccarat:pending:player-7', JSON.stringify({
      idempotencyKey: 'baccarat-recovery-0001', betSide: 'banker', stake: 10,
    }))
    const createRound = vi.fn().mockResolvedValue(settledRound)
    render(<BaccaratGame playerId="player-7" gateway={fakeGateway({ createRound })} />)

    expect(await screen.findByRole('button', { name: 'Deal Again' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledWith('banker', 10, expect.objectContaining({
      idempotencyKey: 'baccarat-recovery-0001', signal: expect.any(AbortSignal),
    }))
  })

  it('shows gateway errors and Deal Again immediately repeats the settled bet with a new request key', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockRejectedValueOnce(new BaccaratGatewayError('Table closed.', 'table-closed', 503)).mockResolvedValue(settledRound)
    const gateway = fakeGateway({ createRound })
    render(<BaccaratGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: /Banker/ }))
    const stake = screen.getByRole('spinbutton', { name: 'Stake' })
    await user.clear(stake)
    await user.type(stake, '10')
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Table closed.')

    await user.click(screen.getByRole('button', { name: 'Retry Deal' }))
    await user.click(await screen.findByRole('button', { name: 'Deal Again' }))
    expect(await screen.findByRole('button', { name: 'Deal Again' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledTimes(3)
    expect(createRound.mock.calls[2]?.[0]).toBe('banker')
    expect(createRound.mock.calls[2]?.[1]).toBe(10)
    expect(createRound.mock.calls[2]?.[2]?.idempotencyKey).not.toBe(createRound.mock.calls[1]?.[2]?.idempotencyKey)
  })
})

function fakeGateway(overrides: Partial<BaccaratGateway> = {}): BaccaratGateway {
  return {
    getStatus: vi.fn().mockResolvedValue(status),
    createRound: vi.fn().mockResolvedValue(settledRound),
    ...overrides,
  }
}

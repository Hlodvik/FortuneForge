// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { KenoGame } from './KenoGame'
import { KenoGatewayError, type KenoGateway, type KenoRound } from './contracts'

afterEach(() => { cleanup(); sessionStorage.clear() })

describe('KenoGame', () => {
  it('retries an unavailable table connection without requiring a page refresh', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockRejectedValueOnce(new Error('Offline.')).mockResolvedValueOnce({ available: true, balance: 1_000, mode: 'free-play-keno' })
    render(<KenoGame gateway={fakeGateway({ getStatus })} />)

    await user.click(await screen.findByRole('button', { name: 'Retry connection' }))

    expect(await screen.findByText('Keno ready')).toBeTruthy()
    expect(getStatus).toHaveBeenCalledTimes(2)
  })

  it('does not misrepresent a deliberately unavailable table as a connection failure', async () => {
    render(<KenoGame gateway={fakeGateway({ getStatus: vi.fn().mockRejectedValue(new KenoGatewayError('Not ready.', 'keno-disabled', 503)) })} />)

    expect((await screen.findByRole('alert')).textContent).toContain('temporarily unavailable')
    expect(screen.queryByRole('button', { name: 'Retry connection' })).toBeNull()
  })

  it('selects a number and reports the selected count', async () => {
    const user = userEvent.setup()
    render(<KenoGame gateway={fakeGateway()} />)

    await screen.findByText('Keno ready')

    await user.click(screen.getByRole('button', { name: 'Number 18' }))

    expect(screen.getByRole('button', { name: 'Number 18' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByText(/1 of 10 numbers selected/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Draw Keno' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('prevents selecting more than ten numbers', async () => {
    const user = userEvent.setup()
    render(<KenoGame gateway={fakeGateway()} />)

    await screen.findByText('Keno ready')

    for (let number = 1; number <= 10; number++)
      await user.click(screen.getByRole('button', { name: `Number ${number}` }))

    expect(screen.getByText(/10 of 10 numbers selected/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Number 11' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('clears every selected number', async () => {
    const user = userEvent.setup()
    render(<KenoGame gateway={fakeGateway()} />)
    await screen.findByText('Keno ready')
    await user.click(screen.getByRole('button', { name: 'Number 1' }))
    await user.click(screen.getByRole('button', { name: 'Number 80' }))

    await user.click(screen.getByRole('button', { name: 'Clear selection' }))

    expect(screen.getByText(/0 of 10 numbers selected/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Number 1' }).getAttribute('aria-pressed')).toBe('false')
    expect((screen.getByRole('button', { name: 'Clear selection' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('plays the canonical selected ticket and shows its returned draw and hit count', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway()
    render(<KenoGame gateway={gateway} initialSelection={[15, 3, 7]} />)

    await screen.findByText('Keno ready')

    await user.click(screen.getByRole('button', { name: 'Draw Keno' }))

    expect(gateway.createRound).toHaveBeenCalledWith({ ticket: { numbers: [3, 7, 15] } }, expect.objectContaining({ idempotencyKey: expect.stringMatching(/^keno-/) }))
    expect(await screen.findByText('2 hits')).toBeTruthy()
    expect(screen.getByText('2 of 3 picks hit.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Number 3, hit' }).className).toContain('is-hit')
    expect(screen.getByRole('button', { name: 'Number 15, missed' }).className).toContain('is-missed')
    expect(screen.getByRole('button', { name: 'Number 21, drawn' }).className).toContain('is-drawn')
    expect(screen.getByLabelText('Keno draw').textContent).toContain('Draw: 3, 7, 21')
  })

  it('keeps the draw action disabled until at least one number is selected', async () => {
    const gateway = fakeGateway()
    render(<KenoGame gateway={gateway} />)
    await screen.findByText('Keno ready')

    expect((screen.getByRole('button', { name: 'Draw Keno' }) as HTMLButtonElement).disabled).toBe(true)
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(screen.getByText(/select at least one to enable draw/i)).toBeTruthy()
  })

  it('presents gateway failures accessibly', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockRejectedValue(new KenoGatewayError('Keno is temporarily unavailable.')) })
    render(<KenoGame gateway={gateway} initialSelection={[1]} />)
    await screen.findByText('Keno ready')

    await user.click(screen.getByRole('button', { name: 'Draw Keno' }))

    expect((await screen.findByRole('alert')).textContent).toContain('Keno is temporarily unavailable.')
  })

  it('reuses the original ticket key when a failed draw is retried', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockRejectedValueOnce(new KenoGatewayError('Connection lost.')).mockResolvedValueOnce(completedRound)
    const gateway = fakeGateway({ createRound })
    render(<KenoGame gateway={gateway} initialSelection={[3, 7, 15]} />)
    await screen.findByText('Keno ready')

    await user.click(screen.getByRole('button', { name: 'Draw Keno' }))
    await screen.findByRole('alert')
    await user.click(screen.getByRole('button', { name: 'Draw Keno' }))
    await screen.findByText('2 hits')

    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).toBe(createRound.mock.calls[0]?.[1]?.idempotencyKey)
  })

  it('replays the exact player-scoped pending draw after a reload', async () => {
    sessionStorage.setItem('fortuneforge:keno:pending:player-7', JSON.stringify({
      idempotencyKey: 'keno-recovery-0001', numbers: [3, 7, 15],
    }))
    const createRound = vi.fn().mockResolvedValue(completedRound)
    render(<KenoGame playerId="player-7" gateway={fakeGateway({ createRound })} />)

    expect(await screen.findByText('2 hits')).toBeTruthy()
    expect(createRound).toHaveBeenCalledWith({ ticket: { numbers: [3, 7, 15] } }, expect.objectContaining({
      idempotencyKey: 'keno-recovery-0001', signal: expect.any(AbortSignal),
    }))
  })
})

const completedRound: KenoRound = {
  roundId: 'keno-11', balance: 1_000, phase: 'completed',
  ticket: { numbers: [3, 7, 15] },
  draw: { numbers: [3, 7, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38] },
  hitCount: 2,
  outcome: null,
}

function fakeGateway(overrides: Partial<KenoGateway> = {}): KenoGateway {
  return { getStatus: vi.fn().mockResolvedValue({ available: true, balance: 1_000, mode: 'free-play-keno' }), createRound: vi.fn().mockResolvedValue(completedRound), ...overrides }
}

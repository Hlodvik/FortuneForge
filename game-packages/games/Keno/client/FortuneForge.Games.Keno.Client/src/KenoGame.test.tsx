// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { KenoGame } from './KenoGame'
import { KenoGatewayError, type KenoGateway, type KenoRound, type KenoStatus } from './contracts'

afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear(); vi.unstubAllGlobals() })

describe('KenoGame', () => {
  it('retries an unavailable table connection without requiring a page refresh', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockRejectedValueOnce(new Error('Offline.')).mockResolvedValueOnce(availableStatus)
    render(<KenoGame gateway={fakeGateway({ getStatus })} />)
    await user.click(await screen.findByRole('button', { name: 'Retry connection' }))
    expect(await screen.findByRole('combobox', { name: 'Keno wager' })).toBeTruthy()
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
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Number 18' }))
    expect(screen.getByRole('button', { name: 'Number 18' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByText(/1 of 10 numbers selected/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Draw' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('plays one quiet synthesized effect for each ticket-selection action', async () => {
    const user = userEvent.setup()
    const audio = installAudioContextMock()
    render(<KenoGame gateway={fakeGateway()} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })

    await user.click(screen.getByRole('button', { name: 'Number 18' }))
    await user.click(screen.getByRole('button', { name: 'Number 18' }))
    await user.click(screen.getByRole('button', { name: 'Number 42' }))
    await user.click(screen.getByRole('button', { name: 'Clear selection' }))

    expect(audio.createOscillator).toHaveBeenCalledTimes(8)
    expect(audio.start).toHaveBeenCalledTimes(8)
    expect(audio.stop).toHaveBeenCalledTimes(8)
    expect(audio.peakGain).toHaveBeenCalledWith(.04, 1.008)
    expect(audio.peakGain).toHaveBeenCalledWith(.032, 1.008)
  })

  it('prevents selecting more than ten numbers', async () => {
    render(<KenoGame gateway={fakeGateway()} initialSelection={[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    expect(screen.getByText(/10 of 10 numbers selected/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Number 11' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('clears every selected number', async () => {
    const user = userEvent.setup()
    render(<KenoGame gateway={fakeGateway()} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Number 1' }))
    await user.click(screen.getByRole('button', { name: 'Number 80' }))
    await user.click(screen.getByRole('button', { name: 'Clear selection' }))
    expect(screen.getByText(/0 of 10 numbers selected/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Number 1' }).getAttribute('aria-pressed')).toBe('false')
    expect((screen.getByRole('button', { name: 'Clear selection' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('plays the canonical selected ticket and shows payout, hits, and updated balance', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway()
    render(<KenoGame gateway={gateway} initialSelection={[15, 3, 7]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    expect(screen.getByRole('region', { name: 'Keno ticket' }).className).not.toContain('ff-keno__board--has-round')
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    expect(gateway.createRound).toHaveBeenCalledWith({ ticket: { numbers: [3, 7, 15] }, wager: 1 }, expect.objectContaining({ idempotencyKey: expect.stringMatching(/^keno-/) }))
    await screen.findByText('Round result', {}, { timeout: 5_000 })
    expect(screen.getByRole('region', { name: 'Keno ticket' }).className).toContain('ff-keno__board--has-round')
    expect(screen.queryByRole('button', { name: 'Repeat this ticket' })).toBeNull()
    expect(screen.getByText('R2.00 WIN', { selector: '.ff-keno__result strong' })).toBeTruthy()
    expect(screen.getByText('2 of 3 picks hit.')).toBeTruthy()
    expect(screen.getByText('Wager R1.00 · Balance R1,001.00')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Number 3, hit' }).className).toContain('is-hit')
    expect(screen.getByRole('button', { name: 'Number 15, missed' }).className).toContain('is-missed')
    expect(screen.getByRole('button', { name: 'Number 21, drawn' }).className).toContain('is-drawn')
    expect(screen.getByLabelText('Keno draw').textContent).toContain('Draw: 3, 7, 21')
  })

  it('keeps the draw action disabled until at least one number is selected', async () => {
    const gateway = fakeGateway()
    render(<KenoGame gateway={gateway} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    expect((screen.getByRole('button', { name: 'Draw' }) as HTMLButtonElement).disabled).toBe(true)
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(screen.getByText(/select at least one to enable draw/i)).toBeTruthy()
    expect(screen.queryByText('Choose a ticket size')).toBeNull()
  })

  it('presents gateway failures accessibly', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockRejectedValue(new KenoGatewayError('Keno is temporarily unavailable.')) })
    render(<KenoGame gateway={gateway} initialSelection={[1]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Keno is temporarily unavailable.')
  })

  it('reuses the original ticket key when a failed draw is retried', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockRejectedValueOnce(new KenoGatewayError('Connection lost.')).mockResolvedValueOnce(completedRound)
    render(<KenoGame gateway={fakeGateway({ createRound })} initialSelection={[3, 7, 15]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    await screen.findByRole('alert')
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    await screen.findByText('Round result', {}, { timeout: 5_000 })
    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).toBe(createRound.mock.calls[0]?.[1]?.idempotencyKey)
  })

  it('replays the exact player-scoped pending draw after a reload', async () => {
    sessionStorage.setItem('fortuneforge:keno:pending:player-7', JSON.stringify({ idempotencyKey: 'keno-recovery-0001', numbers: [3, 7, 15], wager: 1 }))
    const createRound = vi.fn().mockResolvedValue(completedRound)
    render(<KenoGame playerId="player-7" gateway={fakeGateway({ createRound })} />)
    await screen.findByText('Round result', {}, { timeout: 5_000 })
    expect(createRound).toHaveBeenCalledWith({ ticket: { numbers: [3, 7, 15] }, wager: 1 }, expect.objectContaining({
      idempotencyKey: 'keno-recovery-0001', signal: expect.any(AbortSignal),
    }))
  })

  it('shows the current ticket payout table and submits the chosen wager', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockResolvedValue({ ...completedRound, wager: 5, payout: 10, net: 5, balance: 1_005 })
    render(<KenoGame gateway={fakeGateway({ createRound })} initialSelection={[3, 7, 15]} />)
    const wagerSelect = await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.selectOptions(wagerSelect, '5')
    expect(screen.getByRole('table', { name: '3-spot Keno payouts' })).toBeTruthy()
    expect(screen.getByText('R135.00')).toBeTruthy()
    expect(screen.getByText(/R5.00 ticket · balance R1,000.00/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    expect(createRound).toHaveBeenCalledWith({ ticket: { numbers: [3, 7, 15] }, wager: 5 }, expect.anything())
    expect(await screen.findByText('R10.00 WIN', {}, { timeout: 5_000 })).toBeTruthy()
  })

  it('describes a stake return without presenting it as a win', async () => {
    const user = userEvent.setup()
    const returnedRound = { ...completedRound, payout: 1, net: 0, balance: 1_000 }
    render(<KenoGame gateway={fakeGateway({ createRound: vi.fn().mockResolvedValue(returnedRound) })} initialSelection={[3, 7, 15]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    const result = await screen.findByText('R1.00 RETURNED', {}, { timeout: 5_000 })
    expect(result.className).toContain('is-return')
    expect(result.className).not.toContain('is-win')
  })

  it('shows a full selected ticket on the board without duplicate ticket or quick-pick controls', async () => {
    render(<KenoGame gateway={fakeGateway()} initialSelection={[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    expect(screen.getByText(/10 of 10 numbers selected/)).toBeTruthy()
    expect(screen.getAllByRole('button', { pressed: true })).toHaveLength(10)
    expect(screen.queryByText('Quick Pick')).toBeNull()
    expect(screen.queryByLabelText('Current Keno ticket')).toBeNull()
    expect(screen.getByRole('table', { name: '10-spot Keno payouts' })).toBeTruthy()
    expect(screen.getByText('R200,000.00')).toBeTruthy()
  })

  it('reveals the draw in stages and locks ticket changes until it is complete', async () => {
    const user = userEvent.setup()
    const audio = installAudioContextMock()
    render(<KenoGame gateway={fakeGateway()} initialSelection={[3, 7, 15]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    expect(await screen.findByText('Drawing live')).toBeTruthy()
    expect(audio.drawStart).toHaveBeenCalledTimes(1)
    expect((screen.getByRole('button', { name: /^Number 3$/ }) as HTMLButtonElement).disabled).toBe(true)
    await screen.findByText('Round result', {}, { timeout: 5_000 })
    expect(screen.getByText('R2.00 WIN', { selector: '.ff-keno__result strong' })).toBeTruthy()
    await waitFor(() => expect(audio.start).toHaveBeenCalledTimes(2))
  })

  it('keeps the completed ticket selected for the next draw without a redundant repeat button', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockResolvedValue(completedRound)
    render(<KenoGame gateway={fakeGateway({ createRound })} initialSelection={[3, 7, 15]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    await screen.findByText('Round result', {}, { timeout: 5_000 })
    expect(screen.queryByRole('button', { name: 'Repeat this ticket' })).toBeNull()
    expect(screen.getAllByRole('button', { pressed: true })).toHaveLength(3)
    const drawAgain = screen.getByRole('button', { name: 'Draw again' }) as HTMLButtonElement
    await waitFor(() => expect(drawAgain.disabled).toBe(false), { timeout: 3_000 })
    await user.click(drawAgain)
    expect(createRound).toHaveBeenCalledTimes(2)
    expect(createRound.mock.calls[1]?.[0]).toEqual({ ticket: { numbers: [3, 7, 15] }, wager: 1 })
    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).not.toBe(createRound.mock.calls[0]?.[1]?.idempotencyKey)
  })

  it('clears drawn states after a completed round while keeping the ticket selected', async () => {
    const user = userEvent.setup()
    render(<KenoGame gateway={fakeGateway()} initialSelection={[3, 7, 15]} />)
    await screen.findByRole('combobox', { name: 'Keno wager' })
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    await screen.findByText('Round result', {}, { timeout: 5_000 })
    expect(screen.getByRole('button', { name: 'Number 3, hit' })).toBeTruthy()

    await waitFor(() => expect(screen.getByRole('button', { name: 'Number 3' }).getAttribute('aria-pressed')).toBe('true'), { timeout: 3_000 })
    expect(screen.queryByRole('button', { name: 'Number 3, hit' })).toBeNull()
    expect(screen.getAllByRole('button', { pressed: true })).toHaveLength(3)
    expect(screen.getByRole('button', { name: 'Draw again' })).toBeTruthy()
  })
})

const availableStatus: KenoStatus = {
  available: true, minimumWager: 1, maximumWager: 20, wagerIncrement: 1, balance: 1_000, mode: 'credit-keno',
  paytable: [
    { spots: 1, hits: 1, multiplier: 2.5 },
    { spots: 3, hits: 2, multiplier: 2 }, { spots: 3, hits: 3, multiplier: 27 },
    { spots: 10, hits: 0, multiplier: 5 }, { spots: 10, hits: 5, multiplier: 2 },
    { spots: 10, hits: 6, multiplier: 10 }, { spots: 10, hits: 7, multiplier: 55 },
    { spots: 10, hits: 8, multiplier: 500 }, { spots: 10, hits: 9, multiplier: 4_500 },
    { spots: 10, hits: 10, multiplier: 200_000 },
  ],
}

const completedRound: KenoRound = {
  roundId: 'keno-11', balance: 1_001, phase: 'completed', ticket: { numbers: [3, 7, 15] },
  draw: { numbers: [3, 7, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38] },
  hitCount: 2, wager: 1, payout: 2, net: 1, outcome: 'R2.00 return',
}

function fakeGateway(overrides: Partial<KenoGateway> = {}): KenoGateway {
  return { getStatus: vi.fn().mockResolvedValue(availableStatus), createRound: vi.fn().mockResolvedValue(completedRound), ...overrides }
}

function installAudioContextMock() {
  const start = vi.fn()
  const stop = vi.fn()
  const drawStart = vi.fn()
  const drawStop = vi.fn()
  const peakGain = vi.fn()
  const createOscillator = vi.fn(() => ({
    type: 'sine',
    frequency: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() },
    connect: vi.fn(destination => destination),
    start,
    stop,
  }))
  const createGain = vi.fn(() => ({
    gain: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: peakGain },
    connect: vi.fn(destination => destination),
  }))
  const createBufferSource = vi.fn(() => ({ buffer: null, connect: vi.fn(destination => destination), start: drawStart, stop: drawStop }))
  const audio = {
    state: 'running', currentTime: 1, destination: {}, createOscillator, createGain, createBufferSource,
    decodeAudioData: vi.fn().mockResolvedValue({}), resume: vi.fn().mockResolvedValue(undefined), close: vi.fn().mockResolvedValue(undefined),
    start, stop, peakGain, drawStart, drawStop,
  }
  function AudioContextMock() { return audio }
  vi.stubGlobal('AudioContext', AudioContextMock)
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, arrayBuffer: vi.fn().mockResolvedValue(new ArrayBuffer(8)) }))
  return audio
}

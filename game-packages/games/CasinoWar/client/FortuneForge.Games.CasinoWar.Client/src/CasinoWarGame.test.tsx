// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CasinoWarGame } from './CasinoWarGame'
import { CasinoWarGatewayError, type CasinoWarGateway, type CasinoWarRound, type CasinoWarStatus } from './contracts'

const status: CasinoWarStatus = {
  available: true, minimumPrimaryStake: 1, maximumPrimaryStake: 100, stakeIncrement: 1,
  maximumTieStake: 25, balance: 1_000, mode: 'test',
}

const immediateRound: CasinoWarRound = {
  roundId: 'round-11', balance: 1_008, primaryStake: 10, tieStake: 2, phase: 'completed',
  playerOpeningCard: { rank: 'ace', suit: 'clubs' }, dealerOpeningCard: { rank: 'king', suit: 'diamonds' },
  decision: null, playerWarCard: null, dealerWarCard: null,
  primarySettlement: { disposition: 'win', outcome: 'player-opening-win', totalWagered: 10, totalReturn: 20, profit: 10 },
  tieSettlement: { won: false, disposition: 'loss', outcome: 'player-win', stake: 2, totalReturn: 0, profit: -2 },
}

const awaitingTieRound: CasinoWarRound = {
  roundId: 'round-12', balance: 988, primaryStake: 10, tieStake: 2, phase: 'awaiting-tie-decision',
  playerOpeningCard: { rank: 'queen', suit: 'clubs' }, dealerOpeningCard: { rank: 'queen', suit: 'spades' },
  decision: null, playerWarCard: null, dealerWarCard: null, primarySettlement: null,
  tieSettlement: { won: true, disposition: 'win', outcome: 'tie-decision-required', stake: 2, totalReturn: 22, profit: 20 },
}

const completedWarRound: CasinoWarRound = {
  ...awaitingTieRound,
  balance: 1_030,
  phase: 'completed',
  decision: 'go-to-war',
  playerWarCard: { rank: 'ace', suit: 'hearts' },
  dealerWarCard: { rank: 'king', suit: 'spades' },
  primarySettlement: { disposition: 'win', outcome: 'player-war-win', totalWagered: 20, totalReturn: 30, profit: 10 },
}

afterEach(() => { cleanup(); sessionStorage.clear() })

describe('CasinoWarGame', () => {
  it('retries an unavailable table connection without requiring a page refresh', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockRejectedValueOnce(new Error('Offline.')).mockResolvedValueOnce(status)
    render(<CasinoWarGame gateway={fakeGateway({ getStatus })} />)

    await user.click(await screen.findByRole('button', { name: 'Retry connection' }))

    expect(await screen.findByRole('button', { name: 'Deal' })).toBeTruthy()
    expect(getStatus).toHaveBeenCalledTimes(2)
  })

  it('does not misrepresent a deliberately unavailable table as a connection failure', async () => {
    render(<CasinoWarGame gateway={fakeGateway({ getStatus: vi.fn().mockRejectedValue(new CasinoWarGatewayError('Not ready.', 'casino-war-disabled', 503)) })} />)

    expect((await screen.findByRole('alert')).textContent).toContain('temporarily unavailable')
    expect(screen.queryByRole('button', { name: 'Retry connection' })).toBeNull()
  })

  it('requires nonzero Tie stakes to align to the table increment from zero', async () => {
    const user = userEvent.setup()
    render(<CasinoWarGame gateway={fakeGateway()} />)

    const tie = await screen.findByRole('spinbutton', { name: 'Tie stake (optional)' })
    await user.clear(tie)
    await user.type(tie, '1.5')
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(true)

    await user.clear(tie)
    await user.type(tie, '2')
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('submits both stakes, renders an immediate win, and shows the optional Tie settlement', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue(immediateRound) })
    render(<CasinoWarGame gateway={gateway} />)

    const primary = await screen.findByRole('spinbutton', { name: 'Primary stake' })
    await user.clear(primary)
    await user.type(primary, '10')
    const tie = screen.getByRole('spinbutton', { name: 'Tie stake (optional)' })
    await user.clear(tie)
    await user.type(tie, '2')
    await user.click(screen.getByRole('button', { name: 'Deal' }))

    expect(gateway.createRound).toHaveBeenCalledWith(10, 2, expect.objectContaining({ idempotencyKey: expect.stringMatching(/^casino-war-opening-/) }))
    expect((await screen.findAllByText('Player Opening Win')).length).toBeGreaterThan(0)
    expect(await screen.findByLabelText('ace of clubs')).toBeTruthy()
    expect(await screen.findByText('Tie side bet')).toBeTruthy()
    expect(screen.getByText('-R2.00')).toBeTruthy()
    expect(screen.getByText('+R8.00')).toBeTruthy()
  })

  it('renders an opening tie and sends the surrender decision', async () => {
    const user = userEvent.setup()
    const surrendered: CasinoWarRound = {
      ...awaitingTieRound,
      phase: 'completed', decision: 'surrender',
      primarySettlement: { disposition: 'surrender', outcome: 'player-surrendered', totalWagered: 10, totalReturn: 5, profit: -5 },
    }
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue(awaitingTieRound), decide: vi.fn().mockResolvedValue(surrendered) })
    render(<CasinoWarGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    expect(await screen.findByRole('button', { name: 'Surrender' })).toBeTruthy()
    expect(screen.getByText('Tie wins · +R20.00')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Surrender' }))

    expect(gateway.decide).toHaveBeenCalledWith('round-12', 'surrender', expect.objectContaining({ idempotencyKey: expect.stringMatching(/^casino-war-decision-/) }))
    expect((await screen.findAllByText('Player Surrendered')).length).toBeGreaterThan(0)
  })

  it('sends Go to War and renders the returned War cards and completed settlement', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue(awaitingTieRound), decide: vi.fn().mockResolvedValue(completedWarRound) })
    render(<CasinoWarGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    expect(await screen.findByText(/Go to War adds one matching R10.00 primary stake/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Go to War' }))

    expect(gateway.decide).toHaveBeenCalledWith('round-12', 'go-to-war', expect.objectContaining({ idempotencyKey: expect.stringMatching(/^casino-war-decision-/) }))
    expect((await screen.findAllByText('Player War Win')).length).toBeGreaterThan(0)
    expect(await screen.findByLabelText('ace of hearts')).toBeTruthy()
    expect(await screen.findByLabelText('king of spades')).toBeTruthy()
  })

  it('prevents duplicate deals while busy', async () => {
    const user = userEvent.setup()
    let resolveRound: ((round: CasinoWarRound) => void) | undefined
    const pending = new Promise<CasinoWarRound>(resolve => { resolveRound = resolve })
    const gateway = fakeGateway({ createRound: vi.fn().mockReturnValue(pending) })
    render(<CasinoWarGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    expect((screen.getByRole('button', { name: 'Dealing…' }) as HTMLButtonElement).disabled).toBe(true)
    expect(gateway.createRound).toHaveBeenCalledTimes(1)

    resolveRound?.(immediateRound)
    await screen.findByRole('button', { name: 'New Round' })
  })

  it('repeats the settled stakes without making the player re-enter them', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockResolvedValue(immediateRound)
    render(<CasinoWarGame gateway={fakeGateway({ createRound })} />)

    const primary = await screen.findByRole('spinbutton', { name: 'Primary stake' })
    await user.clear(primary)
    await user.type(primary, '10')
    const tie = screen.getByRole('spinbutton', { name: 'Tie stake (optional)' })
    await user.clear(tie)
    await user.type(tie, '2')
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    await user.click(await screen.findByRole('button', { name: 'Rebet' }))

    expect(createRound).toHaveBeenNthCalledWith(2, 10, 2, expect.objectContaining({
      idempotencyKey: expect.stringMatching(/^casino-war-opening-/),
    }))
  })

  it('restores a pending tie decision for the same player', async () => {
    sessionStorage.setItem('fortuneforge:casino-war:round:player-7', 'round-12')
    const getRound = vi.fn().mockResolvedValue(awaitingTieRound)
    render(<CasinoWarGame playerId="player-7" gateway={fakeGateway({ getRound })} />)

    expect(await screen.findByRole('button', { name: 'Surrender' })).toBeTruthy()
    expect(getRound).toHaveBeenCalledWith('round-12', expect.any(AbortSignal))
  })

  it('replays the exact player-scoped pending opening after a reload', async () => {
    sessionStorage.setItem('fortuneforge:casino-war:pending:player-7', JSON.stringify({
      operation: 'opening', idempotencyKey: 'casino-war-opening-recovery-0001', primaryStake: 10, tieStake: 2,
    }))
    const createRound = vi.fn().mockResolvedValue(awaitingTieRound)
    render(<CasinoWarGame playerId="player-7" gateway={fakeGateway({ createRound })} />)

    expect(await screen.findByRole('button', { name: 'Surrender' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledWith(10, 2, expect.objectContaining({
      idempotencyKey: 'casino-war-opening-recovery-0001', signal: expect.any(AbortSignal),
    }))
  })

  it('shows gateway errors and New Round returns to the stake controls', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockRejectedValueOnce(new CasinoWarGatewayError('Table closed.', 'table-closed', 503)).mockResolvedValueOnce(immediateRound) })
    render(<CasinoWarGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Table closed.')
    expect((screen.getByRole('spinbutton', { name: 'Primary stake' }) as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByRole('spinbutton', { name: 'Tie stake (optional)' }) as HTMLInputElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Retry Deal' }))
    await user.click(await screen.findByRole('button', { name: 'New Round' }))
    const calls = vi.mocked(gateway.createRound).mock.calls
    expect(calls[1]?.[2]?.idempotencyKey).toBe(calls[0]?.[2]?.idempotencyKey)
    expect(screen.getByRole('spinbutton', { name: 'Primary stake' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Deal' })).toBeTruthy()
  })

  it('keeps a failed tie decision on its original choice and request key', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({
      createRound: vi.fn().mockResolvedValue(awaitingTieRound),
      decide: vi.fn().mockRejectedValueOnce(new CasinoWarGatewayError('Connection interrupted.', 'temporary', 503)).mockResolvedValueOnce(completedWarRound),
    })
    render(<CasinoWarGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    await user.click(await screen.findByRole('button', { name: 'Go to War' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Connection interrupted.')
    expect((screen.getByRole('button', { name: 'Surrender' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Retry Go to War' }))

    await screen.findByRole('button', { name: 'New Round' })
    const calls = vi.mocked(gateway.decide).mock.calls
    expect(calls[1]?.[1]).toBe('go-to-war')
    expect(calls[1]?.[2]?.idempotencyKey).toBe(calls[0]?.[2]?.idempotencyKey)
  })
})

function fakeGateway(overrides: Partial<CasinoWarGateway> = {}): CasinoWarGateway {
  return {
    getStatus: vi.fn().mockResolvedValue(status),
    getRound: vi.fn().mockResolvedValue(immediateRound),
    createRound: vi.fn().mockResolvedValue(immediateRound),
    decide: vi.fn().mockResolvedValue(completedWarRound),
    ...overrides,
  }
}

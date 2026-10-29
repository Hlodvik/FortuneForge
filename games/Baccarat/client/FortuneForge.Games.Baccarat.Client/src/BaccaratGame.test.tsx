// @vitest-environment jsdom
import { cleanup, configure, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BaccaratGame } from './BaccaratGame'
import { BaccaratGatewayError, type BaccaratGateway, type BaccaratRound, type BaccaratStatus } from './contracts'

const status: BaccaratStatus = { available: true, minimumStake: .25, maximumStake: 100, stakeIncrement: .25, balance: 1_000, mode: 'test', supportsPairBets: true }
const settledRound: BaccaratRound = {
  roundId: 'round-11', balance: 1_009.5, betSide: 'banker', stake: 10, phase: 'settled',
  playerCards: [{ rank: 'eight', suit: 'spades' }, { rank: 'eight', suit: 'spades' }],
  bankerCards: [{ rank: 'nine', suit: 'clubs' }, { rank: 'king', suit: 'diamonds' }],
  playerTotal: 6, bankerTotal: 9, outcome: 'banker', endedOnNatural: true,
  disposition: 'win', profit: 9.5, totalReturn: 19.5,
}

configure({ asyncUtilTimeout: 2_500 })
afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear() })

describe('BaccaratGame', () => {
  it('leaves the Player and Banker total circles empty until a total is available', async () => {
    render(<BaccaratGame gateway={fakeGateway()} />)

    expect((await screen.findByLabelText('Player total pending')).textContent).toBe('')
    expect(screen.getByLabelText('Banker total pending').textContent).toBe('')
  })

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

    await user.click(await screen.findByRole('button', { name: 'Select R5.00 chip' }))
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: 'Deal' }))

    expect(gateway.createRound).toHaveBeenCalledWith('banker', 10, expect.objectContaining({
      idempotencyKey: expect.stringMatching(/^baccarat-/), playerPairStake: 0, bankerPairStake: 0,
    }))
    expect(await screen.findByText('Banker wins')).toBeTruthy()
    expect(screen.getAllByLabelText('eight of spades')).toHaveLength(2)
    expect(screen.getByText('Natural')).toBeTruthy()
    expect(screen.getByText('+R9.50')).toBeTruthy()
  })

  it('selects a chip denomination without wagering until a felt area is clicked', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockResolvedValue(settledRound)
    render(<BaccaratGame gateway={fakeGateway({ createRound })} />)

    const deal = await screen.findByRole('button', { name: 'Deal' }) as HTMLButtonElement
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
    expect(deal.disabled).toBe(true)
    const chip = screen.getByRole('button', { name: 'Select R5.00 chip' })
    await user.click(chip)
    expect(chip.getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R10.00')
    expect(deal.disabled).toBe(false)
    await user.click(screen.getByRole('button', { name: 'Deal' }))

    expect(createRound).toHaveBeenCalledWith('banker', 10, expect.objectContaining({ idempotencyKey: expect.any(String), playerPairStake: 0, bankerPairStake: 0 }))
  })

  it('adds independent pair wagers and sends the complete bet while showing the combined total', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockResolvedValue({
      ...settledRound,
      stake: 2,
      playerPairStake: .5,
      bankerPairStake: .5,
      playerPairDisposition: 'win',
      bankerPairDisposition: 'loss',
      playerPairProfit: 5.5,
      bankerPairProfit: -.5,
      playerPairTotalReturn: 6,
      bankerPairTotalReturn: 0,
      totalStake: 3,
      combinedProfit: 6.9,
      combinedTotalReturn: 9.9,
    } satisfies BaccaratRound)
    render(<BaccaratGame gateway={fakeGateway({ createRound })} />)

    await user.click(await screen.findByRole('button', { name: 'Select R2.00 chip' }))
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: 'Select R0.50 chip' }))
    await user.click(screen.getByRole('button', { name: /^Player Pair, / }))
    await user.click(screen.getByRole('button', { name: /^Banker Pair, / }))

    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R3.00')
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    expect(createRound).toHaveBeenCalledWith('banker', 2, expect.objectContaining({
      playerPairStake: .5, bankerPairStake: .5, idempotencyKey: expect.any(String),
    }))
    expect(await screen.findByText('+R6.90')).toBeTruthy()
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Win R9.90')
  })

  it('does not allow pair-only wagers to deal, and clearing removes every placed chip', async () => {
    const user = userEvent.setup()
    render(<BaccaratGame gateway={fakeGateway()} />)

    const deal = await screen.findByRole('button', { name: 'Deal' }) as HTMLButtonElement
    await user.click(screen.getByRole('button', { name: /^Player Pair, / }))
    await user.click(screen.getByRole('button', { name: /^Banker Pair, / }))
    expect(deal.disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: /^Player, / }))
    expect(deal.disabled).toBe(false)
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.75')

    await user.click(screen.getByRole('button', { name: 'Clear Baccarat bets' }))
    expect(screen.getByRole('button', { name: /^Player, / }).getAttribute('aria-label')).toContain('R0.00')
    expect(screen.getByRole('button', { name: /^Player Pair, / }).getAttribute('aria-label')).toContain('R0.00')
    expect(screen.getByRole('button', { name: /^Banker Pair, / }).getAttribute('aria-label')).toContain('R0.00')
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
    expect(deal.disabled).toBe(true)
  })

  it('keeps pair wagers unavailable when an older status response omits pair-bet support', async () => {
    const legacyStatus: BaccaratStatus = {
      available: true, minimumStake: .25, maximumStake: 100,
      stakeIncrement: .25, balance: 1_000, mode: 'test',
    }
    const user = userEvent.setup()
    const createRound = vi.fn().mockResolvedValue(settledRound)
    render(<BaccaratGame gateway={fakeGateway({
      getStatus: vi.fn().mockResolvedValue(legacyStatus), createRound,
    })} />)

    const player = await screen.findByRole('button', { name: /^Player, / }) as HTMLButtonElement
    await waitFor(() => expect(player.disabled).toBe(false))
    const playerPair = screen.getByRole('button', { name: /^Player Pair, / }) as HTMLButtonElement
    const bankerPair = screen.getByRole('button', { name: /^Banker Pair, / }) as HTMLButtonElement
    expect(playerPair.disabled).toBe(true)
    expect(bankerPair.disabled).toBe(true)

    await user.click(player)
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.25')
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    expect(createRound).toHaveBeenCalledWith('player', .25, expect.objectContaining({
      playerPairStake: 0, bankerPairStake: 0,
    }))
  })

  it('prevents repeated deals while busy', async () => {
    const user = userEvent.setup()
    let resolveRound: ((round: BaccaratRound) => void) | undefined
    const pendingRound = new Promise<BaccaratRound>(resolve => { resolveRound = resolve })
    const gateway = fakeGateway({ createRound: vi.fn().mockReturnValue(pendingRound) })
    render(<BaccaratGame gateway={gateway} />)

    const deal = await screen.findByRole('button', { name: 'Deal' })
    await user.click(screen.getByRole('button', { name: /^Player, / }))
    await user.click(deal)
    expect((screen.getByRole('button', { name: 'Dealing…' }) as HTMLButtonElement).disabled).toBe(true)
    expect(gateway.createRound).toHaveBeenCalledTimes(1)

    resolveRound?.(settledRound)
    await screen.findByRole('dialog', { name: 'Baccarat result' })
  })

  it('puts a losing profit sign before the Rand symbol', async () => {
    const user = userEvent.setup()
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue({ ...settledRound, disposition: 'loss', profit: -10, totalReturn: 0 }) })
    render(<BaccaratGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: /^Player, / }))
    await user.click(await screen.findByRole('button', { name: 'Deal' }))

    expect(await screen.findByText('-R10.00')).toBeTruthy()
  })

  it('reuses the original deal key when a failed request is retried', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockRejectedValueOnce(new BaccaratGatewayError('Connection lost.')).mockResolvedValueOnce(settledRound)
    const gateway = fakeGateway({ createRound })
    render(<BaccaratGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: /^Player, / }))
    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    await screen.findByRole('alert')
    expect((screen.getByRole('button', { name: 'Select R0.25 chip' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: /^Banker, / }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Retry Deal' }))
    await screen.findByRole('dialog', { name: 'Baccarat result' })

    expect(createRound.mock.calls[1]?.[2]?.idempotencyKey).toBe(createRound.mock.calls[0]?.[2]?.idempotencyKey)
  })

  it('replays the exact player-scoped pending deal after a reload', async () => {
    sessionStorage.setItem('fortuneforge:baccarat:pending:player-7', JSON.stringify({
      idempotencyKey: 'baccarat-recovery-0001', betSide: 'banker', stake: 10, playerPairStake: .25, bankerPairStake: .5,
    }))
    const createRound = vi.fn().mockResolvedValue(settledRound)
    render(<BaccaratGame playerId="player-7" gateway={fakeGateway({ createRound })} />)

    expect(await screen.findByRole('dialog', { name: 'Baccarat result' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledWith('banker', 10, expect.objectContaining({
      idempotencyKey: 'baccarat-recovery-0001', signal: expect.any(AbortSignal),
      playerPairStake: .25, bankerPairStake: .5,
    }))
  })

  it('closes the result into a clean board and lets the player place a fresh wager', async () => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockRejectedValueOnce(new BaccaratGatewayError('Table closed.', 'table-closed', 503)).mockResolvedValue(settledRound)
    const gateway = fakeGateway({ createRound })
    render(<BaccaratGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Select R5.00 chip' }))
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Table closed.')

    await user.click(screen.getByRole('button', { name: 'Retry Deal' }))
    const result = await screen.findByRole('dialog', { name: 'Baccarat result' })
    expect(screen.getAllByLabelText('eight of spades')).toHaveLength(2)
    await user.click(result.querySelector('button')!)
    expect(screen.queryByRole('dialog', { name: 'Baccarat result' })).toBeNull()
    expect(screen.queryAllByLabelText('eight of spades')).toHaveLength(0)
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Balance R1,009.50')
    expect(screen.getByRole('button', { name: /^Banker, / }).querySelectorAll('.ff-baccarat__chip-pile img')).toHaveLength(0)
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: /^Banker, / }))
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    expect(await screen.findByRole('dialog', { name: 'Baccarat result' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledTimes(3)
    expect(createRound.mock.calls[2]?.[0]).toBe('banker')
    expect(createRound.mock.calls[2]?.[1]).toBe(10)
    expect(createRound.mock.calls[2]?.[2]?.idempotencyKey).not.toBe(createRound.mock.calls[1]?.[2]?.idempotencyKey)
    await user.click(screen.getByLabelText('Table amounts'))
    expect(screen.queryByRole('dialog', { name: 'Baccarat result' })).toBeNull()
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
  })
})

function fakeGateway(overrides: Partial<BaccaratGateway> = {}): BaccaratGateway {
  return {
    getStatus: vi.fn().mockResolvedValue(status),
    createRound: vi.fn().mockResolvedValue(settledRound),
    ...overrides,
  }
}

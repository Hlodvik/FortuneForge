// @vitest-environment jsdom
import { act, cleanup, configure, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BaccaratGame } from './BaccaratGame'
import { BaccaratGatewayError, type BaccaratGateway, type BaccaratRound, type BaccaratStatus } from './contracts'

const tableStatus: BaccaratStatus = { available: true, minimumStake: .25, maximumStake: 100, stakeIncrement: .25, balance: 1_000, mode: 'test', supportsPairBets: true }
const round: BaccaratRound = {
  roundId: 'ux-round-1', balance: 1_009.5, betSide: 'banker', stake: 10, phase: 'settled',
  playerCards: [{ rank: 'eight', suit: 'spades' }, { rank: 'eight', suit: 'spades' }],
  bankerCards: [{ rank: 'nine', suit: 'clubs' }, { rank: 'king', suit: 'diamonds' }],
  playerTotal: 6, bankerTotal: 9, outcome: 'banker', endedOnNatural: true,
  disposition: 'win', profit: 9.5, totalReturn: 19.5,
}

configure({ asyncUtilTimeout: 2_500 })
afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear(); vi.unstubAllGlobals() })

describe('Baccarat wager accessibility and round controls', () => {
  // This exercises the real deal animation plus four accessible user clicks.
  // Allow headroom when the complete release suite shares the machine.
  it('keeps chip selections locked until the result is dismissed', async () => {
    const user = userEvent.setup()
    render(<BaccaratGame gateway={gateway()} />)

    const chip = screen.getByRole('button', { name: 'Select R5.00 chip' }) as HTMLButtonElement
    await waitFor(() => expect(chip.disabled).toBe(false))
    await user.click(chip)
    await user.click(screen.getByRole('button', { name: /^Player, / }))
    await user.click(screen.getByRole('button', { name: 'Deal' }))

    expect(chip.disabled).toBe(true)
    await screen.findByRole('dialog', { name: 'Baccarat result' })
    expect(chip.disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Continue' }))
    expect(chip.disabled).toBe(false)
  }, 15_000)

  it('shows five chip denominations at once and scrolls from R0.25 through R50', async () => {
    const user = userEvent.setup()
    render(<BaccaratGame gateway={gateway()} />)
    await screen.findByRole('button', { name: 'Select R0.25 chip' })

    expect(screen.getAllByRole('button', { name: /^Select R.* chip$/ })).toHaveLength(5)
    expect((screen.getByRole('button', { name: 'Show lower chip values' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByRole('button', { name: 'Select R0.25 chip' })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Show higher chip values' }))
    await user.click(screen.getByRole('button', { name: 'Show higher chip values' }))
    await user.click(screen.getByRole('button', { name: 'Show higher chip values' }))

    expect(screen.getAllByRole('button', { name: /^Select R.* chip$/ })).toHaveLength(5)
    const highChip = screen.getByRole('button', { name: 'Select R50.00 chip' })
    await user.click(highChip)
    expect(highChip.getAttribute('aria-pressed')).toBe('true')
    expect((screen.getByRole('button', { name: 'Show higher chip values' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Show lower chip values' }))
    expect(screen.getByRole('button', { name: 'Select R1.00 chip' })).toBeTruthy()
  })

  it('shows three chips on narrow screens and keeps the selected value visible when widened', async () => {
    vi.stubGlobal('innerWidth', 390)
    vi.stubGlobal('innerHeight', 844)
    const user = userEvent.setup()
    render(<BaccaratGame gateway={gateway()} />)

    await waitFor(() => expect(screen.getAllByRole('button', { name: /^Select R.* chip$/ })).toHaveLength(3))
    expect(screen.getByRole('button', { name: 'Select R0.25 chip' })).toBeTruthy()
    for (let index = 0; index < 5; index++) await user.click(screen.getByRole('button', { name: 'Show higher chip values' }))
    const highChip = screen.getByRole('button', { name: 'Select R50.00 chip' })
    await user.click(highChip)
    expect(highChip.getAttribute('aria-pressed')).toBe('true')

    vi.stubGlobal('innerWidth', 800)
    fireEvent(window, new Event('resize'))
    await waitFor(() => expect(screen.getAllByRole('button', { name: /^Select R.* chip$/ })).toHaveLength(5))
    expect(screen.getByRole('button', { name: 'Select R50.00 chip' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('caps a chip-built wager at the table maximum without adding extra chips', async () => {
    const user = userEvent.setup()
    render(<BaccaratGame gateway={gateway()} />)
    const player = screen.getByRole('button', { name: /^Player, / }) as HTMLButtonElement
    await waitFor(() => expect(player.disabled).toBe(false))
    for (let index = 0; index < 3; index++) await user.click(screen.getByRole('button', { name: 'Show higher chip values' }))
    await user.click(screen.getByRole('button', { name: 'Select R50.00 chip' }))
    await user.click(player)
    await user.click(player)
    await user.click(player)

    expect(player.getAttribute('aria-label')).toContain('R100.00')
    expect(player.querySelectorAll('.ff-baccarat__chip-pile img')).toHaveLength(2)
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R100.00')
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('prevents main and side-bet chips from exceeding the available balance', async () => {
    const user = userEvent.setup()
    render(<BaccaratGame gateway={gateway({ ...tableStatus, balance: 5 })} />)
    const player = screen.getByRole('button', { name: /^Player, / }) as HTMLButtonElement
    await waitFor(() => expect(player.disabled).toBe(false))
    await user.click(screen.getByRole('button', { name: 'Select R2.00 chip' }))
    await user.click(player)
    await user.click(screen.getByRole('button', { name: /^Player Pair, / }))
    await user.click(screen.getByRole('button', { name: 'Select R1.00 chip' }))
    await user.click(screen.getByRole('button', { name: /^Banker Pair, / }))
    await user.click(player)
    await user.click(screen.getByRole('button', { name: /^Player Pair, / }))
    await user.click(screen.getByRole('button', { name: /^Banker Pair, / }))

    expect(player.getAttribute('aria-label')).toContain('R2.00')
    expect(screen.getByRole('button', { name: /^Player Pair, / }).getAttribute('aria-label')).toContain('R2.00')
    expect(screen.getByRole('button', { name: /^Banker Pair, / }).getAttribute('aria-label')).toContain('R1.00')
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R5.00')
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('announces that the table is loading while its limits and balance are unknown', () => {
    render(<BaccaratGame gateway={{ ...gateway(), getStatus: vi.fn().mockReturnValue(new Promise(() => undefined)) }} />)

    expect(screen.getAllByRole('status').some(element => /loading.*table|opening.*table|connecting/i.test(element.textContent ?? ''))).toBe(true)
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('reveals the settled hand immediately when reduced motion is preferred', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }))
    render(<BaccaratGame gateway={gateway()} />)
    const deal = screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement
    await waitFor(() => expect(screen.getByRole('button', { name: /^Player, / }).hasAttribute('disabled')).toBe(false))
    fireEvent.click(screen.getByRole('button', { name: /^Player, / }))
    expect(deal.disabled).toBe(false)

    await act(async () => { fireEvent.click(deal); await Promise.resolve() })

    expect(screen.getByRole('dialog', { name: 'Baccarat result' })).toBeTruthy()
    expect(screen.getAllByLabelText('eight of spades')).toHaveLength(2)
  })

  it('unlocks a definitely rejected wager and refreshes the available balance before the next deal', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockResolvedValueOnce(tableStatus).mockResolvedValue({ ...tableStatus, balance: 5 })
    const createRound = vi.fn().mockRejectedValueOnce(new BaccaratGatewayError('Not enough credits.', 'insufficient-slot-credits', 409)).mockResolvedValue(round)
    render(<BaccaratGame playerId="ux-player" gateway={{ getStatus, createRound }} />)
    const player = screen.getByRole('button', { name: /^Player, / }) as HTMLButtonElement
    await waitFor(() => expect(player.disabled).toBe(false))
    await user.click(screen.getByRole('button', { name: 'Select R5.00 chip' }))
    await user.click(player)
    await user.click(player)
    await user.click(screen.getByRole('button', { name: 'Deal' }))

    await waitFor(() => expect(getStatus).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(player.disabled).toBe(false))
    expect(sessionStorage.getItem('fortuneforge:baccarat:pending:ux-player')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Retry Deal' })).toBeNull()
    expect(screen.getByLabelText('Table amounts').textContent).toContain('Balance R5.00')
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Clear Baccarat bets' }))
    await user.click(screen.getByRole('button', { name: 'Select R2.00 chip' }))
    await user.click(player)
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    await screen.findByRole('dialog', { name: 'Baccarat result' })
    expect(createRound.mock.calls[1]?.[1]).toBe(2)
    expect(createRound.mock.calls[1]?.[2]?.idempotencyKey).not.toBe(createRound.mock.calls[0]?.[2]?.idempotencyKey)
  })

  it('leaves a definitively invalid restored wager editable instead of trapping it in restoration', async () => {
    sessionStorage.setItem('fortuneforge:baccarat:pending:ux-player', JSON.stringify({
      idempotencyKey: 'baccarat-invalid-recovery', betSide: 'banker', stake: 101,
    }))
    const getStatus = vi.fn().mockResolvedValue(tableStatus)
    const createRound = vi.fn().mockRejectedValueOnce(new BaccaratGatewayError('Choose a stake from R1.00 through R100.00.', 'baccarat-invalid-request', 400))
    render(<BaccaratGame playerId="ux-player" gateway={{ getStatus, createRound }} />)

    const player = await screen.findByRole('button', { name: /^Player, / }) as HTMLButtonElement
    await waitFor(() => expect(player.disabled).toBe(false))
    expect(sessionStorage.getItem('fortuneforge:baccarat:pending:ux-player')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Retry restoration' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Deal' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledTimes(1)
  })
})

function gateway(status = tableStatus): BaccaratGateway {
  return { getStatus: vi.fn().mockResolvedValue(status), createRound: vi.fn().mockResolvedValue(round) }
}

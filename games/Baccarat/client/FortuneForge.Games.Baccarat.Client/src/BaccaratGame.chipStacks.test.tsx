// @vitest-environment jsdom
import { cleanup, configure, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { BaccaratGame } from './BaccaratGame'
import type { BaccaratGateway, BaccaratRound, BaccaratStatus } from './contracts'

const status: BaccaratStatus = {
  available: true, minimumStake: .25, maximumStake: 100, stakeIncrement: .25,
  balance: 1_000, mode: 'test', supportsPairBets: true,
}

configure({ asyncUtilTimeout: 2_500 })
afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear() })

it('keeps the clicked denomination and order for each betting spot', async () => {
  const user = userEvent.setup()
  render(<BaccaratGame gateway={gateway()} />)
  const player = await screen.findByRole('button', { name: /^Player, / })
  const playerPair = screen.getByRole('button', { name: /^Player Pair, / })

  await user.click(player)
  await user.click(player)
  await user.click(screen.getByRole('button', { name: 'Select R0.50 chip' }))
  await user.click(player)
  expect(chipSources(player)).toEqual(['chip-025', 'chip-025', 'chip-050'])
  expect(player.getAttribute('aria-label')).toContain('R1.00')

  await user.click(playerPair)
  await user.click(screen.getByRole('button', { name: 'Select R0.25 chip' }))
  await user.click(playerPair)
  expect(chipSources(playerPair)).toEqual(['chip-050', 'chip-025'])
  expect(chipSources(player)).toEqual(['chip-025', 'chip-025', 'chip-050'])

  const banker = screen.getByRole('button', { name: /^Banker, / })
  await user.click(banker)
  expect(chipSources(player)).toEqual([])
  expect(chipSources(banker)).toEqual(['chip-025'])
  expect(chipSources(playerPair)).toEqual(['chip-050', 'chip-025'])
})

it('appends different clicked denominations and clears the stack', async () => {
  const user = userEvent.setup()
  render(<BaccaratGame gateway={gateway()} />)
  const player = await screen.findByRole('button', { name: /^Player, / })

  await user.click(screen.getByRole('button', { name: 'Select R1.00 chip' }))
  await user.click(player)
  await user.click(screen.getByRole('button', { name: 'Select R0.25 chip' }))
  await user.click(player)
  expect(chipSources(player)).toEqual(['chip-1', 'chip-025'])
  await user.click(screen.getByRole('button', { name: 'Select R0.50 chip' }))
  await user.click(player)
  expect(chipSources(player)).toEqual(['chip-1', 'chip-025', 'chip-050'])
  await user.click(screen.getByRole('button', { name: 'Clear Baccarat bets' }))
  expect(chipSources(player)).toEqual([])
  expect(player.getAttribute('aria-label')).toContain('R0.00')
  expect(screen.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
})

it('keeps clicked chips through settlement, then clears them with the result', async () => {
  const user = userEvent.setup()
  const createRound = vi.fn().mockResolvedValue(settledRound)
  render(<BaccaratGame gateway={{ ...gateway(), createRound }} />)
  const player = await screen.findByRole('button', { name: /^Player, / })
  await user.click(player)
  await user.click(player)
  expect(chipSources(player)).toEqual(['chip-025', 'chip-025'])

  await user.click(screen.getByRole('button', { name: 'Deal' }))
  await screen.findByRole('dialog', { name: 'Baccarat result' })
  expect(chipSources(player)).toEqual(['chip-025', 'chip-025'])
  await user.click(screen.getByRole('button', { name: 'Continue' }))
  await waitFor(() => expect(chipSources(player)).toEqual([]))
  expect(createRound).toHaveBeenCalledTimes(1)
})

it('restores a saved click sequence for an in-flight deal', async () => {
  sessionStorage.setItem('fortuneforge:baccarat:pending:stack-player', JSON.stringify({
    idempotencyKey: 'baccarat-stack-recovery-0001', betSide: 'player', stake: 1,
    playerPairStake: .25, bankerPairStake: 0,
    placedChips: { main: [.25, .25, .5], playerPair: [.25], bankerPair: [] },
  }))
  render(<BaccaratGame playerId="stack-player" gateway={{
    ...gateway(), createRound: vi.fn().mockReturnValue(new Promise(() => {})),
  }} />)

  const player = await screen.findByRole('button', { name: /^Player, / })
  const playerPair = screen.getByRole('button', { name: /^Player Pair, / })
  await waitFor(() => expect(chipSources(player)).toEqual(['chip-025', 'chip-025', 'chip-050']))
  expect(chipSources(playerPair)).toEqual(['chip-025'])
})

const settledRound: BaccaratRound = {
  roundId: 'chip-stack-round', balance: 1_000.5, betSide: 'player', stake: .5,
  phase: 'settled', playerCards: [{ rank: 'eight', suit: 'spades' }, { rank: 'ace', suit: 'hearts' }],
  bankerCards: [{ rank: 'four', suit: 'clubs' }, { rank: 'three', suit: 'diamonds' }],
  playerTotal: 9, bankerTotal: 7, outcome: 'player', endedOnNatural: true,
  disposition: 'win', profit: .5, totalReturn: 1,
}

function chipSources(button: HTMLElement): string[] {
  return Array.from(button.querySelectorAll('.ff-baccarat__chip-pile img'))
    .map(image => image.getAttribute('src')?.match(/chip-(?:025|050|\d+)/)?.[0] ?? '')
}

function gateway(): BaccaratGateway {
  return {
    getStatus: vi.fn().mockResolvedValue(status),
    createRound: vi.fn().mockRejectedValue(new Error('No deal expected in this test.')),
  }
}

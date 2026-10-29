// @vitest-environment jsdom
import { cleanup, render, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { BaccaratGame } from './BaccaratGame'
import type { BaccaratGateway, BaccaratRound, BaccaratStatus } from './contracts'

const status: BaccaratStatus = {
  available: true, minimumStake: .25, maximumStake: 100, stakeIncrement: .25,
  balance: 100, mode: 'test', supportsPairBets: true,
}
const round: BaccaratRound = {
  roundId: 'roadmap-session-round', balance: 100.25, betSide: 'player', stake: .25, phase: 'settled',
  playerCards: [{ rank: 'nine', suit: 'spades' }, { rank: 'king', suit: 'hearts' }],
  bankerCards: [{ rank: 'four', suit: 'clubs' }, { rank: 'queen', suit: 'diamonds' }],
  playerTotal: 9, bankerTotal: 4, outcome: 'player', endedOnNatural: true,
  disposition: 'win', profit: .25, totalReturn: .5,
}
const gateway: BaccaratGateway = {
  getStatus: vi.fn().mockResolvedValue(status),
  createRound: vi.fn().mockResolvedValue(round),
}

afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear(); vi.unstubAllGlobals() })

it('keeps the roadmap only for the open Baccarat table session', async () => {
  vi.stubGlobal('matchMedia', () => ({ matches: true }))
  localStorage.setItem('fortuneforge:baccarat:history:roadmap-player', JSON.stringify([{
    roundId: 'old-visit', outcome: 'banker', cardsUsed: 4, natural: false,
  }]))
  localStorage.setItem('fortuneforge:baccarat:shoe:roadmap-player', '120')
  const user = userEvent.setup()
  const table = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  const stats = () => table.container.querySelector('.ff-baccarat__inline-road .ff-baccarat__road-stats')?.textContent

  const player = await table.findByRole('button', { name: /^Player, / }) as HTMLButtonElement
  await waitFor(() => expect(player.disabled).toBe(false))
  await user.click(player)
  await user.click(table.getByRole('button', { name: /^Deal(?: cards)?$/i }))
  await waitFor(() => expect(stats()).toContain('Total 1'))
  expect(localStorage.getItem('fortuneforge:baccarat:history:roadmap-player')).toBeNull()
  expect(localStorage.getItem('fortuneforge:baccarat:shoe:roadmap-player')).toBeNull()

  table.unmount()
  const reopened = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect(reopened.container.querySelector('.ff-baccarat__inline-road .ff-baccarat__road-stats')?.textContent).toContain('Total 0'))
})

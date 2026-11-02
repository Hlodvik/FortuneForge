// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StrictMode } from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { BaccaratGame } from './BaccaratGame'
import { BaccaratGatewayError, type BaccaratGateway, type BaccaratRound, type BaccaratStatus } from './contracts'

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

const tableKey = 'fortuneforge:baccarat:table:roadmap-player'
const pendingKey = 'fortuneforge:baccarat:pending:roadmap-player'

beforeEach(() => {
  window.history.replaceState(null, '', '/games/baccarat')
  vi.stubGlobal('matchMedia', () => ({ matches: true }))
  navigation('navigate')
  vi.mocked(gateway.createRound).mockClear()
})
afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear(); vi.restoreAllMocks(); vi.unstubAllGlobals() })

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

it('restores unsubmitted chips and the selected denomination on reload without dealing', async () => {
  const user = userEvent.setup()
  const table = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect((table.getByRole('button', { name: /^Player, / }) as HTMLButtonElement).disabled).toBe(false))
  await user.click(table.getByRole('button', { name: 'Select R5.00 chip' }))
  for (let index = 0; index < 4; index++) await user.click(table.getByRole('button', { name: /^Player, / }))
  await user.click(table.getByRole('button', { name: 'Select R1.00 chip' }))
  for (let index = 0; index < 2; index++) await user.click(table.getByRole('button', { name: /^Player Pair, / }))
  await user.click(table.getByRole('button', { name: 'Select R2.00 chip' }))
  await user.click(table.getByRole('button', { name: /^Banker Pair, / }))
  const sources = chipSources(table.container)
  const snapshot = await storedTable()

  table.unmount()
  expect(sessionStorage.getItem(tableKey)).toBeNull()
  sessionStorage.setItem(tableKey, snapshot)
  navigation('reload')
  const restored = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect((restored.getByRole('button', { name: 'Player, 1:1, R20.00' }) as HTMLButtonElement).disabled).toBe(false))
  expect(restored.getByRole('button', { name: 'Player Pair, 11:1, R2.00' })).toBeTruthy()
  expect(restored.getByRole('button', { name: 'Banker Pair, 11:1, R2.00' })).toBeTruthy()
  expect(restored.getByRole('button', { name: 'Select R2.00 chip' }).getAttribute('aria-pressed')).toBe('true')
  expect(restored.getByLabelText('Table amounts').textContent).toContain('Bet R24.00')
  expect(chipSources(restored.container)).toEqual(sources)
  expect(gateway.createRound).not.toHaveBeenCalled()
})

it('preserves the settled hand, roadmap and shoe on reload indefinitely through StrictMode', async () => {
  const user = userEvent.setup()
  const table = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect((table.getByRole('button', { name: /^Player, / }) as HTMLButtonElement).disabled).toBe(false))
  await user.click(table.getByRole('button', { name: /^Player, / }))
  await user.click(table.getByRole('button', { name: /^Deal$/ }))
  await table.findByRole('dialog', { name: 'Baccarat result' })
  await waitFor(() => expect(table.container.querySelector('.ff-baccarat__road-stats')?.textContent).toContain('Total 1'))
  const snapshot = await storedTable()
  const cards = table.getAllByRole('img').map(card => card.getAttribute('aria-label'))
  const sources = chipSources(table.container)
  const shoe = table.container.querySelector('.ff-baccarat__shoe-progress')?.textContent

  table.unmount()
  sessionStorage.setItem(tableKey, snapshot)
  vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 365 * 24 * 60 * 60 * 1000)
  navigation('reload')
  const refreshedGateway = { ...gateway, getStatus: vi.fn().mockResolvedValue({ ...status, balance: 150 }) }
  const restored = render(<StrictMode><BaccaratGame gateway={refreshedGateway} playerId="roadmap-player" /></StrictMode>)
  await restored.findByRole('dialog', { name: 'Baccarat result' })
  await waitFor(() => expect(restored.getByLabelText('Table amounts').textContent).toContain('Balance R150.00'))
  expect(restored.container.querySelector('.ff-baccarat__road-stats')?.textContent).toContain('Total 1')
  expect(restored.container.querySelector('.ff-baccarat__shoe-progress')?.textContent).toBe(shoe)
  expect(restored.getAllByRole('img').map(card => card.getAttribute('aria-label'))).toEqual(cards)
  expect(chipSources(restored.container)).toEqual(sources)
  expect(sessionStorage.getItem(tableKey)).not.toBeNull()
  expect(gateway.createRound).toHaveBeenCalledTimes(1)
  await user.click(restored.getByRole('button', { name: 'Continue' }))
  expect(restored.queryByRole('dialog', { name: 'Baccarat result' })).toBeNull()
  expect(restored.container.querySelector('.ff-baccarat__road-stats')?.textContent).toContain('Total 1')
  expect(restored.getByLabelText('Table amounts').textContent).toContain('Balance R150.00')
})

it.each([
  { type: 'navigate', entry: '/games/baccarat' },
  { type: 'back_forward', entry: '/games/baccarat' },
  { type: 'reload', entry: '/home' },
] as const)('starts fresh after $type at $entry even if the browser retains a display snapshot', async ({ type, entry }) => {
  const user = userEvent.setup()
  const table = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect((table.getByRole('button', { name: /^Player, / }) as HTMLButtonElement).disabled).toBe(false))
  await user.click(table.getByRole('button', { name: 'Select R5.00 chip' }))
  await user.click(table.getByRole('button', { name: /^Player, / }))
  const snapshot = await storedTable()
  table.unmount()
  sessionStorage.setItem(tableKey, snapshot)
  navigation(type, entry)
  const reopened = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect((reopened.getByRole('button', { name: 'Player, 1:1, R0.00' }) as HTMLButtonElement).disabled).toBe(false))
  expect(reopened.getByRole('button', { name: 'Select R0.25 chip' }).getAttribute('aria-pressed')).toBe('true')
  expect(reopened.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
  expect(reopened.container.querySelector('.ff-baccarat__road-stats')?.textContent).toContain('Total 0')
  expect(gateway.createRound).not.toHaveBeenCalled()
})

it('starts a new table when returning from the browser back-forward cache', async () => {
  const user = userEvent.setup()
  const table = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect((table.getByRole('button', { name: /^Player, / }) as HTMLButtonElement).disabled).toBe(false))
  await user.click(table.getByRole('button', { name: /^Player, / }))
  await user.click(table.getByRole('button', { name: /^Deal$/ }))
  await table.findByRole('dialog', { name: 'Baccarat result' })
  await user.click(table.getByRole('button', { name: 'Continue' }))
  await user.click(table.getByRole('button', { name: 'Select R5.00 chip' }))
  await user.click(table.getByRole('button', { name: /^Player, / }))
  expect(table.container.querySelector('.ff-baccarat__road-stats')?.textContent).toContain('Total 1')
  const returned = new Event('pageshow')
  Object.defineProperty(returned, 'persisted', { value: true })
  fireEvent(window, returned)

  await waitFor(() => expect((table.getByRole('button', { name: 'Player, 1:1, R0.00' }) as HTMLButtonElement).disabled).toBe(false))
  expect(table.getByRole('button', { name: 'Select R0.25 chip' }).getAttribute('aria-pressed')).toBe('true')
  expect(table.container.querySelector('.ff-baccarat__road-stats')?.textContent).toContain('Total 0')
  expect(table.container.querySelector('.ff-baccarat__shoe-progress')?.textContent).toContain('416 cards')
  expect(table.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
  expect(gateway.createRound).toHaveBeenCalledTimes(1)
})

it.each([
  { name: 'invalid JSON', value: '{broken' },
  { name: 'invalid table data', value: JSON.stringify({ version: 1, round: {}, stake: -20, history: 'broken' }) },
])('discards $name on reload and opens a usable empty table', async ({ value }) => {
  sessionStorage.setItem(tableKey, value)
  navigation('reload')
  const table = render(<BaccaratGame gateway={gateway} playerId="roadmap-player" />)
  await waitFor(() => expect((table.getByRole('button', { name: 'Player, 1:1, R0.00' }) as HTMLButtonElement).disabled).toBe(false))
  expect(table.container.querySelector('.ff-baccarat__road-stats')?.textContent).toContain('Total 0')
  expect(table.getByLabelText('Table amounts').textContent).toContain('Bet R0.00')
  expect(gateway.createRound).not.toHaveBeenCalled()
})

it('keeps an unresolved paid deal recoverable after leaving with the original request key', async () => {
  const createRound = vi.fn().mockRejectedValueOnce(new BaccaratGatewayError('Connection interrupted', 'temporary', 503)).mockResolvedValue(round)
  const recoveringGateway = { getStatus: gateway.getStatus, createRound }
  const user = userEvent.setup()
  const table = render(<BaccaratGame gateway={recoveringGateway} playerId="roadmap-player" />)
  await waitFor(() => expect((table.getByRole('button', { name: /^Player, / }) as HTMLButtonElement).disabled).toBe(false))
  await user.click(table.getByRole('button', { name: /^Player, / }))
  await user.click(table.getByRole('button', { name: /^Deal$/ }))
  await table.findByText('Connection interrupted')
  const pending = sessionStorage.getItem(pendingKey)
  expect(pending).not.toBeNull()
  table.unmount()
  expect(sessionStorage.getItem(tableKey)).toBeNull()
  expect(sessionStorage.getItem(pendingKey)).toBe(pending)

  const reopened = render(<BaccaratGame gateway={recoveringGateway} playerId="roadmap-player" />)
  await reopened.findByRole('dialog', { name: 'Baccarat result' })
  expect(createRound).toHaveBeenCalledTimes(2)
  expect(createRound.mock.calls[1]?.[2]?.idempotencyKey).toBe(createRound.mock.calls[0]?.[2]?.idempotencyKey)
  expect(sessionStorage.getItem(pendingKey)).toBeNull()
})

function navigation(type: 'navigate' | 'reload' | 'back_forward', entry = window.location.href) {
  vi.spyOn(performance, 'getEntriesByType').mockReturnValue([{ type, name: new URL(entry, window.location.href).href } as PerformanceNavigationTiming])
}

async function storedTable() {
  let snapshot: string | null = null
  await waitFor(() => { snapshot = sessionStorage.getItem(tableKey); expect(snapshot).not.toBeNull() })
  return snapshot!
}

function chipSources(container: HTMLElement) {
  return Array.from(container.querySelectorAll('.ff-baccarat__placed-chip img'), image => image.getAttribute('src'))
}

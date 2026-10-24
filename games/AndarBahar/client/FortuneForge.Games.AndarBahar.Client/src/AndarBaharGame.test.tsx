// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AndarBaharGame } from './AndarBaharGame'
import { type AndarBaharCard, type AndarBaharGateway, type AndarBaharRound, type AndarBaharStatus } from './contracts'
import { writePending } from './andarBaharSession'
import { playAndarBaharAudio } from './andarBaharAudio'

vi.mock('./andarBaharAudio', () => ({ playAndarBaharAudio: vi.fn(), stopAndarBaharAudio: vi.fn() }))
const player = 'table-control-test'
const status: AndarBaharStatus = { available: false, minimumStake: .5, maximumStake: 100, stakeIncrement: .5, balance: 100, mode: 'development-practice-andar-first' }
function gateway(available = false) {
  return {
    getStatus: vi.fn<AndarBaharGateway['getStatus']>().mockResolvedValue({ ...status, available }),
    getLatestRound: vi.fn<AndarBaharGateway['getLatestRound']>().mockResolvedValue(null),
    getRound: vi.fn<AndarBaharGateway['getRound']>(),
    createRound: vi.fn<AndarBaharGateway['createRound']>(),
  } satisfies AndarBaharGateway
}

beforeEach(() => {
  localStorage.clear(); sessionStorage.clear(); vi.clearAllMocks()
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
  HTMLDialogElement.prototype.showModal = vi.fn(function(this: HTMLDialogElement) { this.open = true })
  HTMLDialogElement.prototype.close = vi.fn(function(this: HTMLDialogElement) { this.open = false })
})
afterEach(() => { cleanup(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('table controls', () => {
  it('uses supplied faces for revealed Joker, both hands and result cards without exposing undealt cards', async () => {
    const api = gateway(true)
    const round: AndarBaharRound = {
      roundId: 'shared-face-deal', phase: 'settled', joker: { rank: 'queen', suit: 'hearts' },
      deals: [
        { number: 1, side: 'andar', card: { rank: 'seven', suit: 'clubs' } },
        { number: 2, side: 'bahar', card: { rank: 'ten', suit: 'spades' } },
        { number: 3, side: 'andar', card: { rank: 'queen', suit: 'diamonds' } },
      ],
      winner: 'andar', side: 'andar', stake: 5, disposition: 'win', profit: 4.5, totalReturn: 9.5, balance: 104.5,
    }
    api.createRound.mockResolvedValue(round)
    const face = vi.fn((card: AndarBaharCard) => <div data-testid="shared-face">{card.rank}-{card.suit}</div>)
    const { container } = render(<AndarBaharGame gateway={api} playerId={player} renderCardFace={face} />)
    await waitFor(() => expect(api.getLatestRound).toHaveBeenCalledOnce())
    expect(face).not.toHaveBeenCalled()
    expect(container.querySelector('.ff-ab__card--back')).not.toBeNull()
    expect(screen.queryByTestId('shared-face')).toBeNull()

    vi.useFakeTimers()
    fireEvent.click(screen.getByRole('button', { name: /^Bet on Andar/ }))
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Deal cards' })); await Promise.resolve() })
    expect(screen.getAllByTestId('shared-face').map(node => node.textContent)).toEqual(['queen-hearts'])
    expect(face.mock.calls.every(([card]) => card === round.joker)).toBe(true)

    face.mockClear()
    await act(async () => { vi.advanceTimersByTime(700) })
    expect(screen.getAllByTestId('shared-face').map(node => node.textContent)).toEqual(['seven-clubs', 'queen-hearts'])
    expect(face.mock.calls.every(([card]) => card === round.joker || card === round.deals[0].card)).toBe(true)
    expect(screen.queryByText('ten-spades')).toBeNull()
    expect(screen.queryByText('queen-diamonds')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Skip deal' }))
    expect(within(screen.getByRole('region', { name: 'Andar cards' })).getAllByTestId('shared-face').map(node => node.textContent)).toEqual(['seven-clubs', 'queen-diamonds'])
    expect(within(screen.getByRole('region', { name: 'Bahar cards' })).getByTestId('shared-face').textContent).toBe('ten-spades')
    const result = screen.getByRole('dialog', { name: 'Andar wins' })
    expect(within(result).getAllByTestId('shared-face').map(node => node.textContent)).toEqual(['queen-hearts', 'queen-diamonds'])
    expect(container.querySelector('.ff-ab__card--back')).toBeNull()
  })

  it('shows one game title with compact sound/help controls and no duplicate site chrome', async () => {
    const api = gateway(true)
    const { container } = render(<AndarBaharGame gateway={api} playerId={player} />)
    await waitFor(() => expect(api.getLatestRound).toHaveBeenCalledOnce())
    expect(screen.getAllByRole('heading', { level: 1, name: 'Andar Bahar' })).toHaveLength(1)
    expect(container.querySelector('.ff-ab__header')?.textContent).toBe('Andar Bahar?')
    expect(container.querySelector('.ff-ab__brand')).toBeNull()
    expect(container.querySelector('.ff-ab__table-head')).toBeNull()
    expect(container.querySelector('.ff-ab__table-mode')).toBeNull()
    expect(container.querySelector('.ff-ab__limits')).toBeNull()
    expect(container.textContent).not.toMatch(/FORTUNEFORGE|THE TABLE COLLECTION|SOLO TABLE|CONNECTING/)
    expect(screen.queryByRole('button', { name: 'Recent results' })).toBeNull()
    expect(screen.getByRole('button', { name: 'How to play' }).textContent).toBe('?')
    expect(screen.getByRole('button', { name: 'Mute sound' })).toBeTruthy()
  })

  it('puts connected betting limits only inside the help dialog', async () => {
    const api = gateway(true)
    api.getStatus.mockResolvedValue({ ...status, available: true, minimumStake: 1, maximumStake: 75, stakeIncrement: 1 })
    const { container } = render(<AndarBaharGame gateway={api} playerId={player} />)
    await waitFor(() => expect(api.getLatestRound).toHaveBeenCalledOnce())
    expect(container.textContent).not.toContain('R75.00')
    fireEvent.click(screen.getByRole('button', { name: 'How to play' }))
    const dialog = screen.getByRole('dialog', { name: 'One Joker. Two sides.' })
    expect(within(dialog).getByRole('heading', { name: 'Betting limits' })).toBeTruthy()
    expect(dialog.textContent).toContain('R1.00 minimum, R75.00 maximum per round, in R1.00 increments.')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close rules' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('keeps Restore deal usable for a pending wager while new bets are disabled', async () => {
    const api = gateway()
    const key = 'andar-bahar-disabled-existing-key'
    writePending(`fortuneforge:andar-bahar:pending:${player}`, { key, side: 'andar', stake: 1, chips: [1] })
    api.createRound.mockRejectedValueOnce(new Error('interrupted')).mockImplementationOnce(() => new Promise(() => undefined))
    render(<AndarBaharGame gateway={api} playerId={player} />)
    const restore = await screen.findByRole('button', { name: 'Restore deal' })
    expect((restore as HTMLButtonElement).disabled).toBe(false)
    expect((screen.getByRole('button', { name: /^Bet on Andar/ }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(restore)
    await waitFor(() => expect(api.createRound).toHaveBeenCalledTimes(2))
    expect(api.createRound.mock.calls[1][2].idempotencyKey).toBe(key)
  })

  it('does not offer a new deal when the table is disabled without a pending key', async () => {
    const api = gateway()
    render(<AndarBaharGame gateway={api} playerId={player} />)
    await waitFor(() => expect(api.getLatestRound).toHaveBeenCalledOnce())
    expect((screen.getByRole('button', { name: 'Deal cards' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Deal cards' }))
    expect(api.createRound).not.toHaveBeenCalled()
  })

  it('plays one dry click for utility buttons without doubling a chip sound', async () => {
    const api = gateway(true)
    render(<AndarBaharGame gateway={api} playerId={player} />)
    const chip = screen.getByRole('button', { name: 'R1.00 chip' })
    await waitFor(() => expect((chip as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(chip)
    expect(playAndarBaharAudio).toHaveBeenLastCalledWith('chip', false)
    vi.mocked(playAndarBaharAudio).mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'How to play' }))
    expect(playAndarBaharAudio).toHaveBeenCalledExactlyOnceWith('click', false)
  })
})

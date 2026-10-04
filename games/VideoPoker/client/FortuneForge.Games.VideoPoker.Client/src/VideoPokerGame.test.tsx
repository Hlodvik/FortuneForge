// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { VideoPokerGame } from './VideoPokerGame'
import { VideoPokerGatewayError, type VideoPokerGateway, type VideoPokerRound, type VideoPokerStatus } from './contracts'

const status: VideoPokerStatus = { available: true, minimumCoinsWagered: 1, maximumCoinsWagered: 5, coinValue: 1, balance: 100 }
const awaitingRound: VideoPokerRound = {
  roundId: 'round-7', balance: 100, coinsWagered: 3, handCount: 1, wager: 3, phase: 'awaiting-draw',
  initialCards: [
    { rank: 'ace', suit: 'clubs' }, { rank: 'two', suit: 'diamonds' }, { rank: 'three', suit: 'hearts' },
    { rank: 'four', suit: 'spades' }, { rank: 'five', suit: 'clubs' },
  ],
  heldPositions: [], finalCards: null, handRank: null, payout: null, finalHands: null, handRanks: null, handPayouts: null,
}
const settledRound: VideoPokerRound = {
  ...awaitingRound, balance: 112, phase: 'completed', finalCards: awaitingRound.initialCards,
  handRank: 'straight', payout: 12, finalHands: [awaitingRound.initialCards], handRanks: ['straight'], handPayouts: [12],
}

afterEach(() => { cleanup(); sessionStorage.clear() })

describe('VideoPokerGame', () => {
  it('retries an unavailable table connection without requiring a page refresh', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockRejectedValueOnce(new Error('Offline.')).mockResolvedValueOnce(status)
    render(<VideoPokerGame gateway={fakeGateway({ getStatus })} />)

    const retry = await screen.findByRole('button', { name: 'Retry connection' })
    expect(retry.className).toContain('ff-video-poker__primary')
    await user.click(retry)

    expect(await screen.findByRole('button', { name: 'Deal' })).toBeTruthy()
    expect(getStatus).toHaveBeenCalledTimes(2)
  })

  it('does not misrepresent a deliberately unavailable table as a connection failure', async () => {
    render(<VideoPokerGame gateway={fakeGateway({ getStatus: vi.fn().mockRejectedValue(new VideoPokerGatewayError('Not ready.', 'video-poker-disabled', 503)) })} />)

    expect((await screen.findByRole('alert')).textContent).toContain('temporarily unavailable')
    expect(screen.queryByRole('button', { name: 'Retry connection' })).toBeNull()
  })

  it('shows the exact Jacks or Better payout schedule for the selected coin wager', async () => {
    const user = userEvent.setup()
    render(<VideoPokerGame gateway={fakeGateway()} />)

    const wager = await screen.findByRole('combobox', { name: 'Coin wager' })
    await user.selectOptions(wager, '5')

    expect(screen.getByRole('table', { name: 'Jacks or Better paytable' })).toBeTruthy()
    expect(screen.getByRole('cell', { name: '4,000 coins' })).toBeTruthy()
    expect(screen.getByRole('rowheader', { name: 'Jacks or Better' })).toBeTruthy()
    expect(screen.getByText('A pair of jacks, queens, kings, or aces.')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Video Poker basic guide' }))
    expect(screen.getByText('Tap the cards you want to keep. A held card stays in your final hand.')).toBeTruthy()
    expect(screen.getByText('Press Draw once. Every card you did not hold is replaced.')).toBeTruthy()
  })

  it('deals five cards, toggles holds, and sends zero-based positions when drawing', async () => {
    const user = userEvent.setup()
    const completedRound: VideoPokerRound = { ...awaitingRound, phase: 'completed', heldPositions: [0, 2], finalCards: awaitingRound.initialCards, handRank: 'straight', payout: 12, finalHands: [awaitingRound.initialCards], handRanks: ['straight'], handPayouts: [12] }
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue(awaitingRound), draw: vi.fn().mockResolvedValue(completedRound) })
    render(<VideoPokerGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    expect(gateway.createRound).toHaveBeenCalledWith(1, expect.objectContaining({ idempotencyKey: expect.stringMatching(/^video-poker-deal-/) }))
    await screen.findByRole('button', { name: 'Draw' })
    expect(screen.getAllByRole('button', { name: /^Hold / })).toHaveLength(5)

    await screen.findByRole('button', { name: 'Draw' })
    await user.click(screen.getByRole('button', { name: 'Hold ace of clubs' }))
    await user.click(screen.getByRole('button', { name: 'Hold three of hearts' }))
    expect(screen.getByRole('button', { name: 'Release ace of clubs' }).getAttribute('aria-pressed')).toBe('true')

    await user.click(screen.getByRole('button', { name: 'Draw' }))
    expect(gateway.draw).toHaveBeenCalledWith('round-7', [0, 2], expect.objectContaining({ idempotencyKey: expect.stringMatching(/^video-poker-draw-/) }))
    await screen.findByText('Straight', { selector: '.ff-video-poker__completed-hand strong' })
    screen.getByText('R12.00 total won')
    expect(screen.getByRole('button', { name: 'New Hand' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Deal Again' })).toBeNull()
  })

  it('shows busy feedback while a deal is pending and disables repeated deals', async () => {
    const user = userEvent.setup()
    let resolveRound: ((round: VideoPokerRound) => void) | undefined
    const pendingRound = new Promise<VideoPokerRound>(resolve => { resolveRound = resolve })
    const gateway = fakeGateway({ createRound: vi.fn().mockReturnValue(pendingRound) })
    render(<VideoPokerGame gateway={gateway} />)

    const deal = await screen.findByRole('button', { name: 'Deal' })
    await user.click(deal)
    expect((screen.getByRole('button', { name: 'Dealing…' }) as HTMLButtonElement).disabled).toBe(true)
    expect(gateway.createRound).toHaveBeenCalledTimes(1)

    resolveRound?.(awaitingRound)
    await screen.findByRole('button', { name: 'Draw' })
  })

  it('selects five hands and presents each independently settled result', async () => {
    const user = userEvent.setup()
    const fiveHands = Array.from({ length: 5 }, () => awaitingRound.initialCards)
    const completedRound: VideoPokerRound = {
      ...awaitingRound,
      handCount: 5,
      wager: 15,
      phase: 'completed',
      finalCards: awaitingRound.initialCards,
      handRank: 'straight',
      payout: 24,
      finalHands: fiveHands,
      handRanks: ['straight', 'pair', 'no-win', 'two-pair', 'flush'],
      handPayouts: [12, 3, 0, 6, 3],
    }
    const createRound = vi.fn().mockResolvedValue({ ...awaitingRound, handCount: 5, wager: 15 })
    const draw = vi.fn().mockResolvedValue(completedRound)
    render(<VideoPokerGame gateway={fakeGateway({ createRound, draw })} />)

    await user.click(await screen.findByRole('button', { name: '5' }))
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    expect(createRound).toHaveBeenCalledWith(1, expect.objectContaining({ handCount: 5 }))
    await user.click(await screen.findByRole('button', { name: 'Draw' }))

    expect(await screen.findByText('R24.00 total won')).toBeTruthy()
    expect(screen.getByText('Hand 5')).toBeTruthy()
  })

  it('shows a service error when the table status cannot load', async () => {
    const gateway = fakeGateway({ getStatus: vi.fn().mockRejectedValue(new VideoPokerGatewayError('Table is closed.', 'table-closed', 503)) })
    render(<VideoPokerGame gateway={gateway} />)

    expect((await screen.findByRole('alert')).textContent).toContain('Table is closed.')
    expect(screen.queryByRole('button', { name: 'Deal' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Retry connection' })).toBeTruthy()
  })

  it('restores a recorded unfinished hand for the same player', async () => {
    sessionStorage.setItem('fortuneforge:video-poker:round:player-7', 'round-7')
    const getRound = vi.fn().mockResolvedValue(awaitingRound)
    render(<VideoPokerGame playerId="player-7" gateway={fakeGateway({ getRound })} />)

    expect(await screen.findByRole('button', { name: 'Draw' })).toBeTruthy()
    expect(getRound).toHaveBeenCalledWith('round-7', expect.any(AbortSignal))
  })

  it('clamps an obsolete restored five-coin wager to the current maximum before the next deal', async () => {
    const user = userEvent.setup()
    sessionStorage.setItem('fortuneforge:video-poker:round:player-7', 'round-7')
    const getStatus = vi.fn().mockResolvedValue({ ...status, minimumCoinsWagered: 2, maximumCoinsWagered: 4 })
    const getRound = vi.fn().mockResolvedValue({ ...settledRound, coinsWagered: 5, wager: 5, payout: 20, handPayouts: [20] })
    const createRound = vi.fn().mockResolvedValue({ ...awaitingRound, coinsWagered: 4, wager: 4 })
    render(<VideoPokerGame playerId="player-7" gateway={fakeGateway({ getStatus, getRound, createRound })} />)

    await user.click(await screen.findByRole('button', { name: 'New Hand' }))

    const wager = screen.getByRole('combobox', { name: 'Coin wager' }) as HTMLSelectElement
    expect(wager.value).toBe('4')
    expect(screen.getByText('Wager R4.00')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(false)
    expect(getRound).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('button', { name: 'Deal' }))
    expect(createRound).toHaveBeenCalledWith(4, expect.objectContaining({ handCount: 1 }))
  })

  it('focuses connection recovery when New Hand removes a restored result without table status', async () => {
    const user = userEvent.setup()
    sessionStorage.setItem('fortuneforge:video-poker:round:player-7', 'round-7')
    const getStatus = vi.fn().mockRejectedValue(new VideoPokerGatewayError('Connection lost.'))
    render(<VideoPokerGame playerId="player-7" gateway={fakeGateway({ getStatus, getRound: vi.fn().mockResolvedValue(settledRound) })} />)

    const newHand = await screen.findByRole('button', { name: 'New Hand' })
    newHand.focus()
    expect(document.activeElement).toBe(newHand)
    await user.click(newHand)

    const retry = screen.getByRole('button', { name: 'Retry connection' })
    expect(screen.queryByRole('combobox', { name: 'Coin wager' })).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(retry))
  })

  it('allows a lower wager and a new deal key after the host definitively rejects insufficient credits', async () => {
    const user = userEvent.setup()
    const getStatus = vi.fn().mockResolvedValueOnce(status).mockResolvedValueOnce({ ...status, balance: 3 })
    const createRound = vi.fn()
      .mockRejectedValueOnce(new VideoPokerGatewayError('Not enough credits.', 'insufficient-slot-credits', 409))
      .mockResolvedValueOnce({ ...awaitingRound, coinsWagered: 2, wager: 2, balance: 1 })
    render(<VideoPokerGame playerId="player-7" gateway={fakeGateway({ getStatus, createRound })} />)

    const wager = await screen.findByRole('combobox', { name: 'Coin wager' }) as HTMLSelectElement
    await user.selectOptions(wager, '5')
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    await screen.findByText('R3.00', { selector: '.ff-video-poker__balance strong' })

    expect(getStatus).toHaveBeenCalledTimes(2)
    expect(wager.disabled).toBe(false)
    expect(sessionStorage.getItem('fortuneforge:video-poker:pending:player-7')).toBeNull()
    expect((screen.getByRole('button', { name: 'Deal' }) as HTMLButtonElement).disabled).toBe(true)
    const rejectedKey = createRound.mock.calls[0]?.[1]?.idempotencyKey
    expect(rejectedKey).toMatch(/^video-poker-deal-/)

    await user.selectOptions(wager, '2')
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    await screen.findByRole('button', { name: 'Draw' })

    expect(createRound).toHaveBeenNthCalledWith(2, 2, expect.objectContaining({ handCount: 1 }))
    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).toMatch(/^video-poker-deal-/)
    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).not.toBe(rejectedKey)
  })

  it('unlocks an edited wager after a restored pending deal is definitively rejected by the host', async () => {
    const user = userEvent.setup()
    const rejectedKey = 'video-poker-deal-recovery-rejected-0001'
    sessionStorage.setItem('fortuneforge:video-poker:pending:player-7', JSON.stringify({ operation: 'deal', idempotencyKey: rejectedKey, coinsWagered: 5, handCount: 1 }))
    const getStatus = vi.fn().mockResolvedValueOnce(status).mockResolvedValueOnce({ ...status, balance: 3 })
    const createRound = vi.fn()
      .mockRejectedValueOnce(new VideoPokerGatewayError('Not enough credits.', 'insufficient-slot-credits', 409))
      .mockResolvedValueOnce({ ...awaitingRound, coinsWagered: 2, wager: 2, balance: 1 })
    render(<VideoPokerGame playerId="player-7" gateway={fakeGateway({ getStatus, createRound })} />)

    await screen.findByText('R3.00', { selector: '.ff-video-poker__balance strong' })
    const wager = screen.getByRole('combobox', { name: 'Coin wager' }) as HTMLSelectElement
    expect(wager.disabled).toBe(false)
    expect(sessionStorage.getItem('fortuneforge:video-poker:pending:player-7')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Retry restoration' })).toBeNull()
    expect(createRound).toHaveBeenNthCalledWith(1, 5, expect.objectContaining({ idempotencyKey: rejectedKey, handCount: 1, signal: expect.any(AbortSignal) }))

    await user.selectOptions(wager, '2')
    await user.click(screen.getByRole('button', { name: 'Deal' }))
    await screen.findByRole('button', { name: 'Draw' })

    expect(createRound).toHaveBeenNthCalledWith(2, 2, expect.objectContaining({ handCount: 1 }))
    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).toMatch(/^video-poker-deal-/)
    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).not.toBe(rejectedKey)
  })

  it('replays the exact player-scoped pending deal after a reload', async () => {
    sessionStorage.setItem('fortuneforge:video-poker:pending:player-7', JSON.stringify({ operation: 'deal', idempotencyKey: 'video-poker-deal-recovery-0001', coinsWagered: 3 }))
    const createRound = vi.fn().mockResolvedValue(awaitingRound)
    render(<VideoPokerGame playerId="player-7" gateway={fakeGateway({ createRound })} />)

    expect(await screen.findByRole('button', { name: 'Draw' })).toBeTruthy()
    expect(createRound).toHaveBeenCalledWith(3, expect.objectContaining({ idempotencyKey: 'video-poker-deal-recovery-0001', signal: expect.any(AbortSignal) }))
  })

  it('does not replay recovery when only the balance callback changes and notifies the latest callback after draw', async () => {
    const user = userEvent.setup()
    sessionStorage.setItem('fortuneforge:video-poker:pending:player-7', JSON.stringify({ operation: 'deal', idempotencyKey: 'video-poker-deal-recovery-0001', coinsWagered: 3, handCount: 1 }))
    const createRound = vi.fn().mockResolvedValue(awaitingRound)
    const getRound = vi.fn().mockResolvedValue(awaitingRound)
    const getStatus = vi.fn().mockResolvedValue(status)
    const draw = vi.fn().mockResolvedValue(settledRound)
    const gateway = fakeGateway({ createRound, getRound, getStatus, draw })
    const initialCallback = vi.fn()
    const latestCallback = vi.fn()
    const { rerender } = render(<VideoPokerGame playerId="player-7" gateway={gateway} onBalanceChange={initialCallback} />)

    await screen.findByRole('button', { name: 'Draw' })
    await waitFor(() => expect(initialCallback).toHaveBeenCalledWith(100))
    expect(sessionStorage.getItem('fortuneforge:video-poker:pending:player-7')).toBeNull()
    expect(sessionStorage.getItem('fortuneforge:video-poker:round:player-7')).toBe('round-7')

    rerender(<VideoPokerGame playerId="player-7" gateway={gateway} onBalanceChange={latestCallback} />)

    expect(createRound).toHaveBeenCalledTimes(1)
    expect(getRound).not.toHaveBeenCalled()
    expect(getStatus).toHaveBeenCalledTimes(1)
    expect(latestCallback).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    await screen.findByRole('button', { name: 'New Hand' })

    expect(initialCallback).toHaveBeenCalledTimes(1)
    expect(latestCallback).toHaveBeenCalledTimes(1)
    expect(latestCallback).toHaveBeenCalledWith(112)
    expect(getRound).not.toHaveBeenCalled()
    expect(createRound).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['a network failure', new VideoPokerGatewayError('Connection lost.')],
    ['a host round conflict', new VideoPokerGatewayError('An unfinished hand already exists.', 'video-poker-round-conflict', 409)],
    ['a server failure with a rejection-like code', new VideoPokerGatewayError('Request outcome unknown.', 'insufficient-slot-credits', 500)],
  ])('reuses the original deal key after %s', async (_description, reason) => {
    const user = userEvent.setup()
    const createRound = vi.fn().mockRejectedValueOnce(reason).mockResolvedValueOnce(awaitingRound)
    const gateway = fakeGateway({ createRound })
    render(<VideoPokerGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    await screen.findByRole('alert')
    expect((screen.getByRole('combobox', { name: 'Coin wager' }) as HTMLSelectElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Retry Deal' }))
    await screen.findByRole('button', { name: 'Draw' })

    expect(createRound.mock.calls[1]?.[1]?.idempotencyKey).toBe(createRound.mock.calls[0]?.[1]?.idempotencyKey)
  })

  it('locks the held cards after a failed draw and retries the original draw request', async () => {
    const user = userEvent.setup()
    const completedRound: VideoPokerRound = { ...awaitingRound, phase: 'completed', heldPositions: [0], finalCards: awaitingRound.initialCards, handRank: 'pair', payout: 3, finalHands: [awaitingRound.initialCards], handRanks: ['pair'], handPayouts: [3] }
    const draw = vi.fn().mockRejectedValueOnce(new VideoPokerGatewayError('Connection interrupted.')).mockResolvedValueOnce(completedRound)
    const gateway = fakeGateway({ createRound: vi.fn().mockResolvedValue(awaitingRound), draw })
    render(<VideoPokerGame gateway={gateway} />)

    await user.click(await screen.findByRole('button', { name: 'Deal' }))
    await screen.findByRole('button', { name: 'Draw' })
    await user.click(screen.getByRole('button', { name: 'Hold ace of clubs' }))
    await user.click(screen.getByRole('button', { name: 'Draw' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Connection interrupted.')
    expect((screen.getByRole('button', { name: 'Release ace of clubs' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Retry Draw' }))

    await screen.findByRole('button', { name: 'New Hand' })
    expect(draw.mock.calls[1]?.[1]).toEqual([0])
    expect(draw.mock.calls[1]?.[2]?.idempotencyKey).toBe(draw.mock.calls[0]?.[2]?.idempotencyKey)
  })
})

function fakeGateway(overrides: Partial<VideoPokerGateway> = {}): VideoPokerGateway {
  return {
    getStatus: vi.fn().mockResolvedValue(status),
    getRound: vi.fn().mockResolvedValue(awaitingRound),
    createRound: vi.fn().mockResolvedValue(awaitingRound),
    draw: vi.fn().mockResolvedValue(awaitingRound),
    ...overrides,
  }
}

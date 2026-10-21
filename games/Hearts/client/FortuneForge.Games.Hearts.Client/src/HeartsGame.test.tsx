// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HeartsGame } from './HeartsGame'
import { HeartsGatewayError, type HeartsCard, type HeartsGateway, type HeartsMatch, type HeartsPaidGateway, type HeartsPaidSession } from './contracts'
import { seatsClockwise } from './heartsHelpers'

const card = (rank: string, suit = 'clubs'): HeartsCard => ({ code: `${rank}|${suit}`, rank, suit, label: rank + ({ clubs: '♣', hearts: '♥', spades: '♠', diamonds: '♦' } as Record<string, string>)[suit] })
const match = (overrides: Partial<HeartsMatch> = {}): HeartsMatch => ({ matchId: 'match-one', targetScore: 100, roundNumber: 1, phase: 'passing', passDirection: 'left', turn: 'north', humanSeat: 'north', yourTurn: false, opponentsThinking: false, heartsBroken: false, submittedPasses: [], players: seatsClockwise.map((seat, index) => ({ seat, displayName: ['Ada', 'Rowan', 'Clover', 'Quinn'][index], handCount: 13, roundScore: 0, matchScore: 0, hasPassed: false })), hand: [card('2'), card('5'), card('K'), card('Q', 'spades')], legalCards: [], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '', ...overrides })
const idle = (): HeartsPaidSession => ({ kind: 'idle', version: 0, balanceCredits: 100, entryOptions: [1, 2, 5, 10, 25], queue: null, table: null })
function atTable(overrides: Partial<HeartsMatch> = {}): HeartsPaidSession {
  const current = match(overrides)
  return { ...idle(), kind: 'match', version: 4, balanceCredits: 99, table: { match: current, entryCredits: 1, potCredits: 4, payoutCredits: 0, winnerSeats: [], players: current.players.map((player, index) => ({ seat: player.seat, displayName: player.displayName, isCurrentPlayer: index === 0, hasLeft: false, entryCredits: 1, payoutCredits: 0 })), actionDeadlineAtUtc: null, completedAtUtc: null } }
}
function gateway(overrides: Partial<HeartsGateway> = {}, paidOverrides: Partial<HeartsPaidGateway> = {}): HeartsGateway {
  return {
    getStatus: vi.fn().mockResolvedValue({ available: true, defaultTargetScore: 100, mode: 'free-play' }),
    startMatch: vi.fn().mockResolvedValue(match()), pass: vi.fn().mockResolvedValue(match({ phase: 'playing' })), playCard: vi.fn().mockResolvedValue(match({ phase: 'playing' })), advance: vi.fn().mockResolvedValue(match()), nextRound: vi.fn().mockResolvedValue(match({ roundNumber: 2 })),
    paid: { getSession: vi.fn().mockResolvedValue(idle()), join: vi.fn().mockResolvedValue(atTable()), getMatch: vi.fn().mockResolvedValue(atTable()), pass: vi.fn().mockResolvedValue(atTable({ phase: 'playing' })), play: vi.fn().mockResolvedValue(atTable({ phase: 'playing' })), advance: vi.fn().mockResolvedValue(atTable()), nextRound: vi.fn().mockResolvedValue(atTable({ roundNumber: 2 })), leave: vi.fn().mockResolvedValue(idle()), getHistory: vi.fn().mockResolvedValue([]), ...paidOverrides }, ...overrides,
  }
}
beforeEach(() => { vi.stubGlobal('Audio', class { volume = 0; play() { return Promise.resolve() } pause() {} addEventListener() {} }); localStorage.setItem('fortuneforge:hearts:muted', 'true') })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); localStorage.clear() })

describe('Hearts table', () => {
  it('does not open or charge for a match on mount and starts free only after a click', async () => {
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} balance={100} />)
    const free = await screen.findByRole('button', { name: 'Play free' })
    await waitFor(() => expect((free as HTMLButtonElement).disabled).toBe(false))
    expect(api.startMatch).not.toHaveBeenCalled(); expect(api.paid!.join).not.toHaveBeenCalled()
    await user.click(free)
    expect(await screen.findByLabelText('Your hand')).toBeTruthy()
    expect(api.startMatch).toHaveBeenCalledWith({ targetScore: 100, difficulty: 'standard' })
    expect(api.paid!.join).not.toHaveBeenCalled()
  })

  it('chooses a fixed buy-in without charging and enters with a unique request key when pressed', async () => {
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} />)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Select R5.00 entry' }) as HTMLButtonElement).disabled).toBe(false))
    await user.click(screen.getByRole('button', { name: 'Select R5.00 entry' }))
    expect(api.paid!.join).not.toHaveBeenCalled()
    expect(screen.getByText('R20.00')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Play for R5.00' }))
    await screen.findByLabelText('Your hand')
    expect(api.paid!.join).toHaveBeenCalledWith(5, expect.objectContaining({ idempotencyKey: expect.stringMatching(/^hearts-/), expectedVersion: 0 }))
  })

  it('resumes a paid hand without a second entry and keeps the viewer at the bottom', async () => {
    const restored = atTable({ humanSeat: 'east' })
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(restored) })
    const rendered = render(<HeartsGame gateway={api} />)
    await screen.findByLabelText('Your hand')
    expect(api.startMatch).not.toHaveBeenCalled(); expect(api.paid!.join).not.toHaveBeenCalled()
    expect(rendered.container.querySelector('.ff-hearts-seat.seat-bottom')?.textContent).toContain('You')
    expect(rendered.container.querySelectorAll('.ff-hearts-seat')).toHaveLength(4)
  })

  it('requires exactly three selected cards and submits the pass once', async () => {
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} />)
    await user.click(await enabledButton('Play free'))
    await user.click(await screen.findByRole('button', { name: 'Two of clubs, select to pass' }))
    await user.click(screen.getByRole('button', { name: 'Five of clubs, select to pass' }))
    expect((screen.getByRole('button', { name: 'Pass cards 2/3' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'King of clubs, select to pass' }))
    await user.click(screen.getByRole('button', { name: 'Queen of spades, select to pass' }))
    expect(screen.getByRole('button', { name: 'Queen of spades, select to pass' }).getAttribute('aria-pressed')).toBe('false')
    await user.click(screen.getByRole('button', { name: 'Pass cards 3/3' }))
    await waitFor(() => expect(api.pass).toHaveBeenCalledOnce())
    expect(api.pass).toHaveBeenCalledWith('match-one', ['2|clubs', '5|clubs', 'K|clubs'])
  })

  it('plays a legal card in one click and does not enable off-suit cards', async () => {
    const user = userEvent.setup(), current = match({ phase: 'playing', yourTurn: true, legalCards: [card('2')] })
    const api = gateway({ startMatch: vi.fn().mockResolvedValue(current) })
    render(<HeartsGame gateway={api} />)
    await user.click(await enabledButton('Play free'))
    const legal = await screen.findByRole('button', { name: 'Two of clubs, play card' })
    expect((screen.getByRole('button', { name: 'Queen of spades' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(legal)
    await waitFor(() => expect(api.playCard).toHaveBeenCalledWith('match-one', '2|clubs'))
  })

  it('locks uncertain paid writes until a read refresh confirms the authoritative table', async () => {
    const user = userEvent.setup(), current = atTable({ phase: 'playing', yourTurn: true, legalCards: [card('2')] })
    const play = vi.fn().mockRejectedValue(new HeartsGatewayError('Connection lost.', 'offline', 0))
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(current), play })
    render(<HeartsGame gateway={api} />)
    await user.click(await screen.findByRole('button', { name: 'Two of clubs, play card' }))
    const refresh = await screen.findByRole('button', { name: 'Refresh table' })
    expect((screen.getByRole('button', { name: 'Two of clubs, play card' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(refresh)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Two of clubs, play card' }) as HTMLButtonElement).disabled).toBe(false))
    expect(play).toHaveBeenCalledOnce()
    expect(play).toHaveBeenCalledWith('match-one', '2|clubs', expect.objectContaining({ expectedVersion: 4 }))
  })

  it('cancels a queued seat and publishes the returned balance', async () => {
    const user = userEvent.setup(), publish = vi.fn()
    const queued: HeartsPaidSession = { ...idle(), kind: 'queue', version: 2, balanceCredits: 99, queue: { matchId: 'q1', entryCredits: 1, potCredits: 1, joinedAtUtc: new Date().toISOString(), message: '', players: [{ seat: 'north', displayName: 'Ada', isCurrentPlayer: true }] } }
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(queued) })
    render(<HeartsGame gateway={api} onBalanceChange={publish} />)
    await user.click(await screen.findByRole('button', { name: 'Cancel & return entry' }))
    await screen.findByRole('button', { name: 'Play free' })
    expect(api.paid!.leave).toHaveBeenCalledWith('q1', expect.objectContaining({ expectedVersion: 2 }))
    expect(publish).toHaveBeenLastCalledWith(100)
  })

  it('shows tied winners and each share from the server settlement', async () => {
    const settled = atTable({ phase: 'complete', winner: 'north', score: { north: 48, east: 48, south: 104, west: 70 } })
    const result = { ...settled, kind: 'result' as const, table: { ...settled.table!, winnerSeats: ['north', 'east'] as const, payoutCredits: 2, completedAtUtc: new Date().toISOString(), players: settled.table!.players.map(player => ({ ...player, payoutCredits: player.seat === 'north' || player.seat === 'east' ? 2 : 0 })) } }
    render(<HeartsGame gateway={gateway({}, { getSession: vi.fn().mockResolvedValue(result) })} />)
    expect(await screen.findByText('You share the win!')).toBeTruthy()
    expect(screen.getAllByText('R2.00').length).toBeGreaterThan(1)
  })

  it('opens bounded instructions and restores focus when the dialog closes', async () => {
    const user = userEvent.setup()
    render(<HeartsGame gateway={gateway()} />)
    const trigger = screen.getByRole('button', { name: 'How to play' })
    await user.click(trigger)
    expect(screen.getByRole('dialog', { name: 'How to play Hearts' })).toBeTruthy()
    expect(screen.getByText(/Tied winners split it/)).toBeTruthy()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(trigger)
  })
})
async function enabledButton(name: string) {
  const button = await screen.findByRole('button', { name })
  await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(false))
  return button
}

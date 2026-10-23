// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HeartsGame } from './HeartsGame'
import { HeartsGatewayError, type HeartsGateway, type HeartsMatch, type HeartsPaidGateway, type HeartsPaidSession } from './contracts'
import { seatsClockwise } from './heartsHelpers'

const idle = (): HeartsPaidSession => ({ kind: 'idle', version: 0, balanceCredits: 100, entryOptions: [1, 2, 5, 10, 25], queue: null, table: null })
function queued(canPlayHouse = true, otherHuman = false): HeartsPaidSession {
  return { ...idle(), kind: 'queue', version: 4, balanceCredits: 99, queue: { matchId: 'house-choice', entryCredits: 1, potCredits: otherHuman ? 2 : 1, joinedAtUtc: '2026-10-22T12:00:00Z', players: [{ seat: 'north', displayName: 'You', isCurrentPlayer: true }, ...(otherHuman ? [{ seat: 'east' as const, displayName: 'Another human', isCurrentPlayer: false }] : [])], message: 'Checking for players.', canPlayHouse } }
}
function houseTable(): HeartsPaidSession {
  const players = seatsClockwise.map((seat, index) => ({ seat, displayName: index ? `House ${seat[0].toUpperCase() + seat.slice(1)}` : 'You', handCount: 13, roundScore: 0, matchScore: 0, hasPassed: false }))
  const match: HeartsMatch = { matchId: 'house-choice', targetScore: 100, roundNumber: 1, phase: 'passing', passDirection: 'left', turn: 'north', humanSeat: 'north', yourTurn: false, opponentsThinking: false, heartsBroken: false, submittedPasses: [], players, hand: [{ code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' }], legalCards: [], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '' }
  return { ...idle(), kind: 'match', version: 5, balanceCredits: 99, table: { match, entryCredits: 1, potCredits: 4, payoutCredits: 0, winnerSeats: [], players: players.map((player, index) => ({ seat: player.seat, displayName: player.displayName, isCurrentPlayer: index === 0, hasLeft: false, entryCredits: 1, payoutCredits: 0 })), actionDeadlineAtUtc: null, completedAtUtc: null } }
}
function api(paidOverrides: Partial<HeartsPaidGateway> = {}): HeartsGateway {
  return {
    getStatus: vi.fn().mockResolvedValue({ available: true, defaultTargetScore: 100, mode: 'free-play' }),
    startMatch: vi.fn(), pass: vi.fn(), playCard: vi.fn(), advance: vi.fn(), nextRound: vi.fn(),
    paid: { getSession: vi.fn().mockResolvedValue(queued()), join: vi.fn().mockResolvedValue(queued()), playHouse: vi.fn().mockResolvedValue(houseTable()), getMatch: vi.fn().mockResolvedValue(queued()), pass: vi.fn(), play: vi.fn(), advance: vi.fn(), nextRound: vi.fn(), leave: vi.fn().mockResolvedValue(idle()), getHistory: vi.fn().mockResolvedValue([]), ...paidOverrides },
  }
}
beforeEach(() => { vi.stubGlobal('Audio', class { volume = 0; play() { return Promise.resolve() } pause() {} addEventListener() {} }); localStorage.setItem('fortuneforge:hearts:muted', 'true') })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); localStorage.clear() })

describe('Hearts explicit house choice', () => {
  it('asks only after the server assessment, and never starts a house table automatically', async () => {
    const gateway = api()
    render(<HeartsGame gateway={gateway} />)
    expect(await screen.findByRole('heading', { name: 'Play against the house?' })).toBeTruthy()
    expect(screen.getByText(/Your entry is already reserved/).textContent).toContain('R4.00 pot')
    expect(screen.getByText(/If a house seat wins/)).toBeTruthy()
    expect(gateway.paid!.playHouse).not.toHaveBeenCalled()
    expect(gateway.paid!.join).not.toHaveBeenCalled()
    expect(screen.queryByLabelText('Your hand')).toBeNull()
  })

  it('allows waiting for humans without consenting to house play or charging again', async () => {
    const user = userEvent.setup(), gateway = api()
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Wait for players' }))
    expect(screen.getByRole('heading', { name: 'Waiting for human players' })).toBeTruthy()
    expect(gateway.paid!.playHouse).not.toHaveBeenCalled()
    expect(gateway.paid!.join).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Cancel & return entry' })).toBeTruthy()
  })

  it('starts clearly labelled house seats only after an explicit click, with a versioned request', async () => {
    const user = userEvent.setup(), gateway = api()
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Play against the house' }))
    await screen.findByLabelText('Your hand')
    expect(gateway.paid!.playHouse).toHaveBeenCalledOnce()
    expect(gateway.paid!.playHouse).toHaveBeenCalledWith('house-choice', expect.objectContaining({ expectedVersion: 4, idempotencyKey: expect.stringMatching(/^hearts-/) }))
    expect(gateway.paid!.join).not.toHaveBeenCalled()
    for (const name of ['House East', 'House South', 'House West']) expect(screen.getByText(name)).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Play against the house?' })).toBeNull()
  })

  it.each([queued(false), queued(true, true)])('does not offer house play before assessment or while another human is waiting', async session => {
    render(<HeartsGame gateway={api({ getSession: vi.fn().mockResolvedValue(session) })} />)
    await screen.findByRole('heading', { name: 'Finding your table' })
    expect(screen.queryByRole('button', { name: 'Play against the house' })).toBeNull()
  })

  it('honours a human arrival returned at confirmation instead of assuming a house game started', async () => {
    const user = userEvent.setup(), waiting = queued(false, true), gateway = api({ playHouse: vi.fn().mockResolvedValue({ ...waiting, notice: 'Another player joined. Waiting for humans.' }) })
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Play against the house' }))
    expect(await screen.findByText('Another human')).toBeTruthy()
    expect(screen.queryByLabelText('Your hand')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Play against the house' })).toBeNull()
    expect(gateway.paid!.playHouse).toHaveBeenCalledOnce()
  })

  it('recovers an uncertain consent by reading the table, not resubmitting it', async () => {
    const user = userEvent.setup(), gateway = api({ getSession: vi.fn().mockResolvedValueOnce(queued()).mockResolvedValue(houseTable()), playHouse: vi.fn().mockRejectedValue(new HeartsGatewayError('Connection lost.', 'offline', 0)) })
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Play against the house' }))
    await user.click(await screen.findByRole('button', { name: 'Refresh table' }))
    await screen.findByLabelText('Your hand')
    expect(gateway.paid!.playHouse).toHaveBeenCalledOnce()
    expect(gateway.paid!.join).not.toHaveBeenCalled()
  })

  it('locks duplicate consent clicks until the authoritative response arrives', async () => {
    const user = userEvent.setup(), gateway = api({ playHouse: vi.fn(() => new Promise<HeartsPaidSession>(() => undefined)) })
    render(<HeartsGame gateway={gateway} />)
    const choose = await screen.findByRole('button', { name: 'Play against the house' })
    await user.dblClick(choose)
    await waitFor(() => expect(gateway.paid!.playHouse).toHaveBeenCalledOnce())
    expect((choose as HTMLButtonElement).disabled).toBe(true)
  })
})

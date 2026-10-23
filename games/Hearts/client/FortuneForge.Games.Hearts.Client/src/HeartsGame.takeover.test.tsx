// @vitest-environment jsdom
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HeartsGame } from './HeartsGame'
import { HeartsGatewayError, type HeartsCard, type HeartsGateway, type HeartsMatch, type HeartsPaidGateway, type HeartsPaidSession } from './contracts'

const seats = ['north', 'east', 'south', 'west'] as const
const card = (rank: string, suit = 'clubs'): HeartsCard => ({
  code: `${rank}|${suit}`, rank, suit,
  label: rank + ({ clubs: '♣', hearts: '♥', diamonds: '♦', spades: '♠' } as Record<string, string>)[suit],
})
const idle = (): HeartsPaidSession => ({ kind: 'idle', version: 0, balanceCredits: 100, entryOptions: [1, 2, 5, 10, 25], queue: null, table: null })

function inheritedTable(humanSeat: 'east' | 'south' = 'east', westName = 'House West'): HeartsPaidSession {
  const score = { north: 35, east: 27, south: 40, west: 62 }
  const roundScore = { north: 0, east: humanSeat === 'east' ? 5 : 0, south: humanSeat === 'south' ? 5 : 0, west: 0 }
  const names = { north: 'Alex', east: 'House East', south: 'House South', west: westName }
  const leader = humanSeat === 'east' ? 'north' : 'east'
  const hand = [card('K'), card('Q'), card('10'), card('7'), card('6'), card('4'), card('3'), card('J', 'diamonds'), card('A', 'hearts'), card('Q', 'spades')]
  const match: HeartsMatch = {
    matchId: 'active-house-table', targetScore: 100, roundNumber: 3, phase: 'playing' as const,
    passDirection: 'across' as const, turn: humanSeat, humanSeat, yourTurn: true,
    opponentsThinking: false, heartsBroken: true, submittedPasses: seats,
    players: seats.map(seat => ({ seat, displayName: seat === humanSeat ? 'You' : names[seat],
      handCount: seat === leader ? 9 : 10, roundScore: roundScore[seat], matchScore: score[seat], hasPassed: true })),
    hand, legalCards: hand.filter(item => item.suit === 'clubs'),
    currentTrick: { leader, plays: [{ seat: leader, card: card('9') }] }, recentTrick: null,
    completedTricks: [
      { number: 1, leader: 'north' as const, winner: humanSeat, points: 3 },
      { number: 2, leader: humanSeat, winner: humanSeat, points: 2 },
      { number: 3, leader: humanSeat, winner: leader, points: 0 },
    ],
    score, roundScore, difficulty: 'standard' as const, winner: null,
    message: 'Your turn. Follow clubs.',
  }
  return {
    ...idle(), kind: 'match', version: 28, balanceCredits: 95,
    notice: `Joined the table in progress. You inherit House ${humanSeat === 'east' ? 'East' : 'South'}’s cards and points; the pot is unchanged.`,
    table: {
      match, entryCredits: 5, potCredits: 20, payoutCredits: 0, winnerSeats: [],
      players: match.players.map(player => ({ seat: player.seat, displayName: player.displayName,
        isCurrentPlayer: player.seat === humanSeat, hasLeft: false, entryCredits: 5, payoutCredits: 0 })),
      actionDeadlineAtUtc: null, completedAtUtc: null,
    },
  }
}

function gateway(paidOverrides: Partial<HeartsPaidGateway> = {}): HeartsGateway {
  const current = inheritedTable()
  return {
    getStatus: vi.fn().mockResolvedValue({ available: true, defaultTargetScore: 100, mode: 'free-play' }),
    startMatch: vi.fn(), pass: vi.fn(), playCard: vi.fn(), advance: vi.fn(), nextRound: vi.fn(),
    paid: {
      getSession: vi.fn().mockResolvedValue(idle()), join: vi.fn().mockResolvedValue(current),
      playHouse: vi.fn(), getMatch: vi.fn().mockResolvedValue(current),
      pass: vi.fn(), play: vi.fn(), advance: vi.fn(), nextRound: vi.fn(), leave: vi.fn(),
      getHistory: vi.fn().mockResolvedValue([]), ...paidOverrides,
    },
  }
}

async function joinExisting(api: HeartsGateway, onBalanceChange = vi.fn()) {
  const user = userEvent.setup()
  const rendered = render(<HeartsGame gateway={api} onBalanceChange={onBalanceChange} />)
  const entry = await screen.findByRole('button', { name: 'Select R5.00 entry' })
  await waitFor(() => expect((entry as HTMLButtonElement).disabled).toBe(false))
  await user.click(entry)
  await user.click(screen.getByRole('button', { name: 'Play for R5.00' }))
  return { user, rendered }
}

function expectOnlyNormalEntry(api: HeartsGateway) {
  expect(api.paid!.join).toHaveBeenCalledOnce()
  expect(api.paid!.join).toHaveBeenCalledWith(5, expect.objectContaining({
    expectedVersion: 0, idempotencyKey: expect.stringMatching(/^hearts-/),
  }))
  expect(api.paid!.playHouse).not.toHaveBeenCalled()
  expect(api.startMatch).not.toHaveBeenCalled()
  expect(api.paid!.pass).not.toHaveBeenCalled()
  expect(api.paid!.play).not.toHaveBeenCalled()
  expect(api.paid!.advance).not.toHaveBeenCalled()
  expect(api.paid!.nextRound).not.toHaveBeenCalled()
}

beforeEach(() => {
  vi.stubGlobal('Audio', class { volume = 0; play() { return Promise.resolve() } pause() {} addEventListener() {} })
  localStorage.setItem('fortuneforge:hearts:muted', 'true')
})
afterEach(() => { cleanup(); localStorage.clear(); vi.unstubAllGlobals() })

describe('Hearts incoming human takes an active House seat', () => {
  it.each(['east', 'south'] as const)('opens the inherited %s cards, scores and existing pot without another house decision', async humanSeat => {
    const inherited = inheritedTable(humanSeat)
    const api = gateway({ join: vi.fn().mockResolvedValue(inherited), getSession: vi.fn().mockResolvedValueOnce(idle()).mockResolvedValue(inherited) })
    const publishBalance = vi.fn()
    const { rendered } = await joinExisting(api, publishBalance)
    const hand = await screen.findByLabelText('Your hand')
    expect(within(hand).getAllByRole('button')).toHaveLength(10)
    expect(within(hand).getByRole('button', { name: 'King of clubs, play card' })).toBeTruthy()
    expect((within(hand).getByRole('button', { name: 'Queen of spades' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: /Pass cards/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Play against the house' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Play against the house?' })).toBeNull()
    expect(screen.getByText(inherited.notice!)).toBeTruthy()
    expect(screen.getByText(/Round 3/)).toBeTruthy()
    expect(screen.getByText('3/13 tricks')).toBeTruthy()
    expect(screen.getByLabelText('Table pot').textContent).toContain('R20.00')
    const footer = rendered.container.querySelector('.ff-hearts-footer') as HTMLElement
    expect(within(footer).getByText('R5.00')).toBeTruthy()
    expect(within(footer).getByText('R95.00')).toBeTruthy()
    expect(publishBalance).toHaveBeenLastCalledWith(95)

    const bottom = rendered.container.querySelector('.ff-hearts-seat.seat-bottom') as HTMLElement
    const inheritedScore = humanSeat === 'east' ? 27 : 40
    expect(bottom.getAttribute('aria-label')).toBe(`You, ${inheritedScore} points, current turn`)
    expect(within(bottom).getByText('5 this round')).toBeTruthy()
    expect(bottom.querySelector('.ff-hearts-seat__cards')).toBeNull()
    const alex = rendered.container.querySelector(`.ff-hearts-seat.seat-${humanSeat === 'east' ? 'right' : 'top'}`) as HTMLElement
    expect(alex.getAttribute('aria-label')).toBe('Alex, 35 points')
    const houseWest = rendered.container.querySelector(`.ff-hearts-seat.seat-${humanSeat === 'east' ? 'top' : 'left'}`) as HTMLElement
    expect(houseWest.getAttribute('aria-label')).toBe('House West, 62 points')
    const trick = screen.getByLabelText('Current trick')
    expect(within(trick).getByLabelText(`${humanSeat === 'east' ? 'Alex' : 'House East'} played Nine of clubs`)).toBeTruthy()
    expectOnlyNormalEntry(api)
  })

  it('recovers a lost join response with an authoritative GET rather than purchasing or choosing the House again', async () => {
    const inherited = inheritedTable('south')
    const api = gateway({
      join: vi.fn().mockRejectedValue(new HeartsGatewayError('The entry response was lost.', 'offline', 0)),
      getSession: vi.fn().mockResolvedValueOnce(idle()).mockResolvedValue(inherited),
    })
    const { user } = await joinExisting(api)
    await user.click(await screen.findByRole('button', { name: 'Refresh table' }))
    await screen.findByLabelText('Your hand')
    expect(screen.getByLabelText('You, 40 points, current turn')).toBeTruthy()
    expect(screen.getByLabelText('Table pot').textContent).toContain('R20.00')
    expect(screen.getByText(inherited.notice!)).toBeTruthy()
    expect(api.paid!.getSession).toHaveBeenCalledTimes(2)
    expect(api.paid!.getSession).toHaveBeenLastCalledWith()
    expect(api.paid!.getMatch).not.toHaveBeenCalled()
    expectOnlyNormalEntry(api)
  })

  it('replaces another House label on a later authoritative poll without changing the viewer’s hand, score or pot', async () => {
    const inherited = inheritedTable('east')
    const replaced = { ...inheritedTable('east', 'Riley'), version: 29 }
    const api = gateway({
      join: vi.fn().mockResolvedValue(inherited),
      getSession: vi.fn().mockResolvedValueOnce(idle()).mockResolvedValue(replaced),
    })
    const { rendered } = await joinExisting(api)
    await screen.findByLabelText('Your hand')
    expect(screen.getByLabelText('House West, 62 points')).toBeTruthy()
    await screen.findByLabelText('Riley, 62 points', {}, { timeout: 3000 })
    expect(screen.queryByLabelText('House West, 62 points')).toBeNull()
    const top = rendered.container.querySelector('.ff-hearts-seat.seat-top') as HTMLElement
    expect(within(top).getByText('Riley')).toBeTruthy()
    expect(screen.getByLabelText('You, 27 points, current turn')).toBeTruthy()
    expect(within(screen.getByLabelText('Your hand')).getAllByRole('button')).toHaveLength(10)
    expect(screen.getByLabelText('Table pot').textContent).toContain('R20.00')
    expectOnlyNormalEntry(api)
  })
})

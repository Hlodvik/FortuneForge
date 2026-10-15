import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'
import {
  actHouseHoldemRound, advanceHouseHoldemRound, getHouseHoldemRound, getHouseHoldemStatus,
  affordableHouseHoldemAction, minimumHouseHoldemBankroll, startHouseHoldemRound,
  toHouseHoldemPlayingCard, validHouseHoldemAnte, validHouseHoldemTrips,
} from './houseHoldemApi'
import type { HouseHoldemRound } from './houseHoldemApi'

vi.mock('../../../features/account/services/accountsApi', () => ({ fetchWithAccountSession: vi.fn() }))
const fetchMock = vi.mocked(fetchWithAccountSession)
const card = { rank: 'ace', suit: 'spades' } as const
const status = { available: true, balance: 100, activeRoundId: null, minimumAnte: 1, maximumAnte: 100, wagerIncrement: 1, maximumTrips: 500 }
const round: HouseHoldemRound = {
  roundId: 'round-one', balance: 97, phase: 'preflop', ante: 1, blind: 1, trips: 1, play: 0, totalWager: 3,
  payout: 0, playerCards: [card, { rank: 'king', suit: 'hearts' }], dealerCards: null, board: [],
  playerHand: null, dealerHand: null, result: null, allowedActions: ['check', 'play-3x', 'play-4x'], canAdvance: false,
  payoutBreakdown: null,
}
function response(value: unknown, ok = true, statusCode = 200): Response {
  return { ok, status: statusCode, json: async () => value } as Response
}

describe('House Hold’em client contract', () => {
  beforeEach(() => fetchMock.mockReset())

  it('reads server-owned limits and enforces valid optional Trips', async () => {
    fetchMock.mockResolvedValue(response(status))
    expect(await getHouseHoldemStatus()).toEqual(status)
    expect(fetchMock).toHaveBeenCalledWith('/api/games/house-holdem/status', expect.objectContaining({ method: 'GET' }))
    expect(validHouseHoldemAnte(1, status)).toBe(true)
    expect(validHouseHoldemAnte(0, status)).toBe(false)
    expect(validHouseHoldemAnte(100, status)).toBe(true)
    expect(validHouseHoldemAnte(101, status)).toBe(false)
    expect(validHouseHoldemTrips(0, status)).toBe(true)
    expect(validHouseHoldemTrips(.5, status)).toBe(false)
    expect(validHouseHoldemTrips(500, status)).toBe(true)
  })

  it('uses idempotent server mutations for deal, player decisions, and staged reveals', async () => {
    fetchMock.mockResolvedValue(response(round))
    await startHouseHoldemRound(1, 1, 'key-one')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/games/house-holdem/rounds', expect.objectContaining({
      method: 'POST', body: JSON.stringify({ ante: 1, trips: 1 }), headers: expect.any(Headers),
    }))
    expect((fetchMock.mock.lastCall![1]!.headers as Headers).get('Idempotency-Key')).toBe('key-one')
    await actHouseHoldemRound('round-one', 'play-4x', 'key-two')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/games/house-holdem/rounds/round-one/actions', expect.objectContaining({ body: JSON.stringify({ action: 'play-4x' }) }))
    await advanceHouseHoldemRound('round-one', 'key-three')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/games/house-holdem/rounds/round-one/advance', expect.objectContaining({ body: '{}' }))
    await getHouseHoldemRound('round-one')
    expect(fetchMock).toHaveBeenLastCalledWith('/api/games/house-holdem/rounds/round-one', expect.objectContaining({ method: 'GET' }))
  })

  it('rejects dealer-card leaks before settlement', async () => {
    fetchMock.mockResolvedValue(response({ ...round, dealerCards: [card, card] }))
    await expect(getHouseHoldemRound('round-one')).rejects.toThrow('invalid response')
  })

  it('converts spelled ranks into shared cards', () => {
    expect(toHouseHoldemPlayingCard(card, 'player', 0)).toEqual({ id: 'player-0-ace-spades', rank: 1, suit: 'spades' })
    expect(toHouseHoldemPlayingCard({ rank: 'two', suit: 'hearts' }, 'board', 1).rank).toBe(2)
    expect(toHouseHoldemPlayingCard({ rank: 'ten', suit: 'clubs' }, 'board', 2).rank).toBe(10)
  })

  it('reserves an eventual 1× Play and gates each Play multiplier against the live balance', () => {
    expect(minimumHouseHoldemBankroll(1, 0)).toBe(3)
    expect(minimumHouseHoldemBankroll(5, 2)).toBe(17)
    expect(affordableHouseHoldemAction({ ...round, balance: 2 }, 'check')).toBe(true)
    expect(affordableHouseHoldemAction({ ...round, balance: 2 }, 'play-3x')).toBe(false)
    expect(affordableHouseHoldemAction({ ...round, balance: 3 }, 'play-3x')).toBe(true)
    expect(affordableHouseHoldemAction({ ...round, balance: 3 }, 'play-4x')).toBe(false)
    expect(affordableHouseHoldemAction({ ...round, balance: 4 }, 'play-4x')).toBe(true)
    expect(affordableHouseHoldemAction({ ...round, balance: 1, allowedActions: ['play-2x', 'check'] }, 'play-2x')).toBe(false)
    expect(affordableHouseHoldemAction({ ...round, balance: 0, allowedActions: ['play-1x', 'fold'] }, 'fold')).toBe(true)
  })
})

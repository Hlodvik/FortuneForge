import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  dealNextCreditHoldemHand,
  getCreditHoldemSession,
  getCreditHoldemStatus,
  joinCreditHoldemQueue,
  leaveCreditHoldemTable,
  postCreditHoldemAction,
  stableCreditHoldemMutation,
  type CreditHoldemMutationResponse,
} from './creditHoldemApi'

describe('credit Hold’em v2 API boundary', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('joins for free with only the authoritative version and idempotency key', async () => {
    const fetchMock = successfulFetch(idleMutation)
    vi.stubGlobal('fetch', fetchMock)

    await joinCreditHoldemQueue(0, 'join-key')

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/texas-holdem/credit/queue')
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('join-key')
    expect(JSON.parse(String(init.body))).toEqual({ expectedVersion: 0, tableRuleId: 'standard' })
    expect(String(init.body)).not.toMatch(/buy.?in|stake|balance|payout|score/i)
  })

  it('accepts fixed blinds and no public buy-in or fee menu', async () => {
    vi.stubGlobal('fetch', successfulFetch({
      available: true,
      minimumStartPlayers: 3,
      maximumSeats: 5,
      minimumRealPlayers: 2,
      smallBlindCredits: 0.5,
      bigBlindCredits: 1,
      actionDeadlineSeconds: 30,
      matchDeadlineSeconds: 900,
      tableRules: [{
        id: 'standard', name: 'Standard', description: 'Automatic blinds · no ante',
        smallBlindCredits: 0.5, bigBlindCredits: 1, anteCredits: 0, maximumTableStackCredits: 100,
      }],
    }))

    const status = await getCreditHoldemStatus()
    expect(status).toMatchObject({ smallBlindCredits: 0.5, bigBlindCredits: 1 })
    expect(status.tableRules?.[0]).toMatchObject({ id: 'standard', maximumTableStackCredits: 100 })
    expect(status).not.toHaveProperty('buyInCredits')
    expect(status).not.toHaveProperty('platformFeePercent')
  })

  it('submits only synchronous action intent and server version', async () => {
    const fetchMock = successfulFetch(idleMutation)
    vi.stubGlobal('fetch', fetchMock)

    await postCreditHoldemAction('match-1', 'raise', 8, 'action-key', 400)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/texas-holdem/credit/matches/match-1/actions')
    expect(JSON.parse(String(init.body))).toEqual({ type: 'raise', expectedVersion: 8, raiseTo: 400 })
    expect(String(init.body)).not.toMatch(/cards|seed|score|payout|balance/i)
  })

  it('uses explicit next-hand and leave commands without a claim command', async () => {
    const fetchMock = successfulFetch(idleMutation)
    vi.stubGlobal('fetch', fetchMock)

    await dealNextCreditHoldemHand('match-1', 9, 'next-key')
    await leaveCreditHoldemTable('match-1', 10, 'leave-key')

    expect(fetchMock.mock.calls.map((call) => call[0])).toEqual([
      '/api/cards/texas-holdem/credit/matches/match-1/next-hand',
      '/api/cards/texas-holdem/credit/matches/match-1/leave',
    ])
    expect(fetchMock.mock.calls.flat().join(' ')).not.toMatch(/claim|refund/i)
  })

  it('reuses a mutation key only for the same uncertain request fingerprint', () => {
    const first = stableCreditHoldemMutation(null, 'action:match:8:call')
    expect(stableCreditHoldemMutation(first, 'action:match:8:call')).toBe(first)
    expect(stableCreditHoldemMutation(first, 'action:match:9:call')).not.toBe(first)
  })

  it('accepts redacted private cards and rejects private implementation metadata', async () => {
    vi.stubGlobal('fetch', successfulFetch(matchSession))
    await expect(getCreditHoldemSession()).resolves.toMatchObject({ kind: 'match' })

    vi.stubGlobal('fetch', successfulFetch({ ...matchSession, dealSeed: 42 }))
    await expect(getCreditHoldemSession()).rejects.toThrow('invalid credit Hold’em session response')
  })
})

const idleMutation: CreditHoldemMutationResponse = {
  session: { contractVersion: 'cards.texas-holdem.credit.v2', kind: 'idle', version: 0 },
  balanceCredits: 100,
}

export const matchSession = {
  contractVersion: 'cards.texas-holdem.credit.v2', kind: 'match', version: 8,
  table: {
    matchId: 'match-1', status: 'active', street: 'flop', handNumber: 2,
    dealerSeat: 1, activeSeat: 0, pot: 450, currentBet: 200,
    minimumRaiseTo: 400, maximumRaiseTo: 2200, shortAllInRaiseTo: null,
    communityCards: [
      { rank: 'A', suit: 'hearts', hidden: false },
      { rank: '10', suit: 'clubs', hidden: false },
      { rank: '2', suit: 'spades', hidden: false },
    ],
    seats: [
      {
        seatId: 'seat-one', displayName: 'Ada', seat: 0, startingStack: 2500,
        stack: 2200, committed: 300, committedRound: 100, status: 'active', lastAction: 'call',
        holeCards: [
          { rank: 'K', suit: 'hearts', hidden: false },
          { rank: 'Q', suit: 'hearts', hidden: false },
        ], handName: null, isCurrentPlayer: true,
      },
      {
        seatId: 'seat-two', displayName: 'Mina', seat: 1, startingStack: 2500,
        stack: 2100, committed: 400, committedRound: 200, status: 'active', lastAction: 'raise',
        holeCards: [{ hidden: true }, { hidden: true }], handName: null, isCurrentPlayer: false,
      },
      {
        seatId: 'seat-three', displayName: 'Nova', seat: 2, startingStack: 2500,
        stack: 2400, committed: 100, committedRound: 0, status: 'folded', lastAction: 'fold',
        holeCards: [{ hidden: true }, { hidden: true }], handName: null, isCurrentPlayer: false,
      },
    ],
    legalActions: ['fold', 'call', 'raise'], winningSeatIds: [], winningAmount: 0,
    startedAtUtc: '2026-08-16T12:00:00Z', matchDeadlineAtUtc: '2026-08-16T13:00:00Z',
    actionDeadlineAtUtc: '2026-08-16T12:00:30Z', remainingActionMilliseconds: 30000,
  },
} as const

function successfulFetch(value: unknown) {
  return vi.fn().mockImplementation(async () => new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }))
}

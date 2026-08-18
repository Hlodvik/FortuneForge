import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  getBlackjackTableSession,
  getBlackjackTableHistory,
  getBlackjackTableStatus,
  joinBlackjackTableQueue,
  postBlackjackTableAction,
  stableBlackjackTableMutation,
  type BlackjackTableMutationResponse,
} from './blackjackTableApi'

describe('Blackjack table API boundary', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('reads the frozen authenticated table status route', async () => {
    const fetchMock = successfulFetch(statusResponse)
    vi.stubGlobal('fetch', fetchMock)

    await expect(getBlackjackTableStatus()).resolves.toMatchObject({
      minimumStartOccupancy: 3,
      tableCapacity: 5,
    })
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/cards/blackjack/table/status')
  })

  it('queues for free with only version and an idempotency header', async () => {
    const fetchMock = successfulFetch(idleMutation)
    vi.stubGlobal('fetch', fetchMock)

    await joinBlackjackTableQueue(0, 'blackjack_table_join_1')

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/blackjack/table/queue')
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('blackjack_table_join_1')
    expect(JSON.parse(String(init.body))).toEqual({ expectedVersion: 0 })
  })

  it('posts only a server-advertised action and version', async () => {
    const fetchMock = successfulFetch(idleMutation)
    vi.stubGlobal('fetch', fetchMock)

    await postBlackjackTableAction('table-public-1', 'stand', 8, 'blackjack_action_1')

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/blackjack/table/tables/table-public-1/actions')
    expect(JSON.parse(String(init.body))).toEqual({ type: 'stand', expectedVersion: 8 })
    expect(String(init.body)).not.toMatch(/score|payout|deck|hidden|balance/i)
  })

  it('reads paid signed history without a claim command', async () => {
    vi.stubGlobal('fetch', successfulFetch([historyItem]))

    await expect(getBlackjackTableHistory()).resolves.toEqual([historyItem])
  })

  it('reuses the same key for the same uncertain mutation', () => {
    const first = stableBlackjackTableMutation(null, 'action:table:8:stand')
    expect(stableBlackjackTableMutation(first, 'action:table:8:stand')).toBe(first)
  })

  it('accepts ordinary opaque seats and a redacted dealer card', async () => {
    vi.stubGlobal('fetch', successfulFetch(tableSession))

    await expect(getBlackjackTableSession()).resolves.toMatchObject({
      contractVersion: 'cards.blackjack.table.v2',
      kind: 'table',
      table: { legalActions: ['hit', 'stand'] },
    })
  })

  it('accepts split-hand and insurance projections with the expanded legal actions', async () => {
    const player = tableSession.table.seats[0]
    const response = {
      ...tableSession,
      table: {
        ...tableSession.table,
        legalActions: ['hit', 'stand', 'double', 'split', 'surrender', 'insurance', 'decline-insurance'],
        seats: [{
          ...player,
          hands: [
            { handNumber: 1, hand: player.hand, wager: 5, totalWager: 5, payout: 0, status: 'stood', outcome: null, lastAction: 'stand', active: false },
            { handNumber: 2, hand: player.hand, wager: 5, totalWager: 5, payout: 0, status: 'playing', outcome: null, lastAction: null, active: true },
          ],
          insuranceWager: 2.5,
          insurancePayout: 0,
        }],
      },
    }
    vi.stubGlobal('fetch', successfulFetch(response))

    await expect(getBlackjackTableSession()).resolves.toMatchObject({
      kind: 'table',
      table: {
        legalActions: expect.arrayContaining(['split', 'surrender', 'insurance', 'decline-insurance']),
        seats: [{ hands: [{ handNumber: 1 }, { handNumber: 2 }], insuranceWager: 2.5 }],
      },
    })
  })

  it.each([
    ['classification', { ...tableSession, table: { ...tableSession.table, seats: tableSession.table.seats.map((seat) => ({ ...seat, isBot: true })) } }],
    ['skill', { ...tableSession, table: { ...tableSession.table, metadata: { skillLevel: 3 } } }],
    ['seed', { ...tableSession, table: { ...tableSession.table, roundSeed: 42 } }],
    ['raw identity', { ...tableSession, table: { ...tableSession.table, seats: tableSession.table.seats.map((seat) => ({ ...seat, actorId: 'actor:1' })) } }],
  ])('rejects forbidden public %s fields recursively', async (_label, response) => {
    vi.stubGlobal('fetch', successfulFetch(response))
    await expect(getBlackjackTableSession()).rejects.toThrow('invalid blackjack table session')
  })
})

const idleMutation: BlackjackTableMutationResponse = {
  session: { contractVersion: 'cards.blackjack.table.v2', kind: 'idle', version: 0 },
  balanceCredits: 100,
}

const statusResponse = {
  available: true,
  minimumWager: 0.5,
  maximumWager: 100,
  wagerIncrement: 0.5,
  minimumStartOccupancy: 3,
  tableCapacity: 5,
  humanGraceSeconds: 5,
  actionDeadlineSeconds: 60,
  dealerRule: 'Dealer stands on all 17s',
  blackjackPayout: '3:2',
  doubleAllowed: true,
  splitAllowed: false,
  insuranceAllowed: false,
}

const emptyHand = { cards: [], score: null, soft: false, blackjack: false, bust: false }

const tableSession = {
  contractVersion: 'cards.blackjack.table.v2',
  kind: 'table',
  version: 8,
  table: {
    tableId: 'table-public-1',
    phase: 'active',
    round: 2,
    dealer: {
      cards: [
        { rank: '9', suit: 'clubs', hidden: false },
        { hidden: true },
      ],
      score: null,
      soft: false,
      blackjack: false,
      bust: false,
    },
    seats: [{
      seatId: 'seat-public-one',
      displayName: 'Ada',
      seat: 0,
      status: 'playing',
      wager: 5,
      totalWager: 5,
      payout: 0,
      outcome: null,
      lastAction: null,
      hand: {
        cards: [
          { rank: 'A', suit: 'spades', hidden: false },
          { rank: '7', suit: 'hearts', hidden: false },
        ],
        score: 18,
        soft: true,
        blackjack: false,
        bust: false,
      },
      isCurrentPlayer: true,
    }, {
      seatId: 'seat-public-two',
      displayName: 'Mina',
      seat: 1,
      status: 'stood',
      wager: 5,
      totalWager: 5,
      payout: 0,
      outcome: null,
      lastAction: 'stand',
      hand: emptyHand,
      isCurrentPlayer: false,
    }],
    activeSeat: 0,
    legalActions: ['hit', 'stand'],
    createdAtUtc: '2026-08-15T12:00:00Z',
    updatedAtUtc: '2026-08-15T12:00:10Z',
    actionDeadlineAtUtc: '2026-08-15T12:00:30Z',
    wagerDeadlineAtUtc: null,
    transition: null,
    nextTransitionAtUtc: null,
    remainingActionMilliseconds: 20_000,
    remainingWagerMilliseconds: 0,
    remainingTransitionMilliseconds: 0,
  },
} as const

const historyItem = {
  resultId: 'a'.repeat(64), game: 'blackjack', mode: 'credit-table',
  matchId: 'table-public-1', tableId: 'table-public-1', round: 2,
  wagerCredits: 5, payoutCredits: 0, netCredits: -5,
  claimStatus: 'completed', settlementStatus: 'paid',
  completedAtUtc: '2026-08-15T12:00:30Z', seen: false, seenAtUtc: null,
} as const

function successfulFetch(value: unknown) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }))
}

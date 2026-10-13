import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  cancelThreeCardPokerQueue, getThreeCardPokerSession, getThreeCardPokerSnapshot,
  getThreeCardPokerStatus, joinThreeCardPokerQueue, postThreeCardPokerAction,
  postThreeCardPokerWager, sitOutThreeCardPokerRound, stableThreeCardPokerMutation,
  toThreeCardPokerPlayingCard, type ThreeCardPokerMutationResponse,
  type ThreeCardPokerPlaySession, type ThreeCardPokerStatus,
} from './threeCardPokerApi'

afterEach(() => vi.unstubAllGlobals())
describe('Three Card Poker API boundary', () => {
  it('reads the authenticated table gate and server paytables', async () => {
    const fetch = successfulFetch(status)
    vi.stubGlobal('fetch', fetch)
    await expect(getThreeCardPokerStatus()).resolves.toEqual(status)
    expect(fetch.mock.calls[0]?.[0]).toBe('/api/cards/three-card-poker/table/status')
  })
  it('joins without a wager and preserves version and idempotency', async () => {
    const fetch = successfulFetch(idle)
    vi.stubGlobal('fetch', fetch)
    await joinThreeCardPokerQueue(0, 'join-1')
    const [path, request] = fetch.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/three-card-poker/table/queue')
    expect(JSON.parse(String(request.body))).toEqual({ expectedVersion: 0 })
    expect(new Headers(request.headers).get('Idempotency-Key')).toBe('join-1')
  })
  it('cancels an encoded ticket without sending account details', async () => {
    const fetch = successfulFetch(idle)
    vi.stubGlobal('fetch', fetch)
    await cancelThreeCardPokerQueue('ticket/1', 3, 'cancel-1')
    const [path, request] = fetch.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/three-card-poker/table/queue/ticket%2F1')
    expect(request.method).toBe('DELETE')
    expect(JSON.parse(String(request.body))).toEqual({ expectedVersion: 3 })
  })
  it('posts Ante and optional Pair Plus only', async () => {
    const fetch = successfulFetch(idle)
    vi.stubGlobal('fetch', fetch)
    await postThreeCardPokerWager('table/1', 5, 1, 8, 'wager-1')
    const [path, request] = fetch.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/three-card-poker/table/tables/table%2F1/wagers')
    expect(JSON.parse(String(request.body))).toEqual({ ante: 5, pairPlus: 1, expectedVersion: 8 })
  })
  it.each(['play', 'fold'] as const)('sends the %s decision without computing a payout', async (type) => {
    const fetch = successfulFetch(idle)
    vi.stubGlobal('fetch', fetch)
    await postThreeCardPokerAction('table-1', type, 9, `action-${type}`)
    const [, request] = fetch.mock.calls[0] as [string, RequestInit]
    expect(JSON.parse(String(request.body))).toEqual({ type, expectedVersion: 9 })
  })
  it('sits out using the authoritative table version', async () => {
    const fetch = successfulFetch(idle)
    vi.stubGlobal('fetch', fetch)
    await sitOutThreeCardPokerRound('table-1', 9, 'sit-1')
    const [path, request] = fetch.mock.calls[0] as [string, RequestInit]
    expect(path).toContain('/tables/table-1/sit-out')
    expect(JSON.parse(String(request.body))).toEqual({ expectedVersion: 9 })
  })
  it('reads a redacted table snapshot and authoritative balance', async () => {
    vi.stubGlobal('fetch', successfulFetch({ session, balanceCredits: 85 }))
    await expect(getThreeCardPokerSnapshot()).resolves.toMatchObject({ session: { kind: 'table', table: { legalActions: ['play', 'fold'] } }, balanceCredits: 85 })
  })
  it('reuses keys for uncertain retries and changes keys for different actions', () => {
    const first = stableThreeCardPokerMutation(null, 'action:table-1:play:9')
    expect(stableThreeCardPokerMutation(first, first.fingerprint)).toBe(first)
    expect(stableThreeCardPokerMutation(first, 'action:table-1:fold:9').idempotencyKey).not.toBe(first.idempotencyKey)
  })
  it.each([
    { ...session, contractVersion: 'cards.three-card-poker.table.v99' },
    { ...session, table: { ...session.table, legalActions: ['hit'] } },
    { ...session, table: { ...session.table, dealer: { cards: [{ hidden: true, rank: 'A' }], rank: null } } },
    { ...session, table: { ...session.table, deck: [] } },
    { ...session, table: { ...session.table, seats: session.table.seats.map((seat) => ({ ...seat, skillLevel: 4 })) } },
    { ...session, table: { ...session.table, seats: session.table.seats.map((seat) => ({ ...seat, actorId: 'user:1' })) } },
    { ...session, table: { ...session.table, seats: session.table.seats.map((seat) => ({ ...seat, hand: { ...seat.hand, cards: [...seat.hand.cards, { hidden: true }] } })) } },
  ])('rejects an invalid or private response', async (response) => {
    vi.stubGlobal('fetch', successfulFetch(response))
    await expect(getThreeCardPokerSession()).rejects.toThrow('invalid Three Card Poker session')
  })
  it('maps visible ranks to shared PlayingCard and never reveals hidden cards', () => {
    expect(toThreeCardPokerPlayingCard({ hidden: false, rank: 'Q', suit: 'hearts' }, 0, 'hand')).toMatchObject({ rank: 12, suit: 'hearts' })
    expect(toThreeCardPokerPlayingCard({ hidden: true }, 0, 'hand')).toBeNull()
  })
})

const status: ThreeCardPokerStatus = {
  available: true, minimumWager: .5, maximumWager: 100, wagerIncrement: .5,
  minimumStartOccupancy: 3, tableCapacity: 5, humanGraceSeconds: 5, actionDeadlineSeconds: 30,
  dealerRule: 'Queen high or better qualifies', pairPlusAllowed: true,
  anteBonusPaytable: 'Straight 1:1; three of a kind 4:1; straight flush 5:1',
  pairPlusPaytable: 'Pair 1:1; flush 3:1; straight 6:1; trips 30:1; straight flush 40:1', pairPlusPaysOnFold: true,
}
const idle: ThreeCardPokerMutationResponse = { session: { contractVersion: 'cards.three-card-poker.table.v1', kind: 'idle', version: 0 }, balanceCredits: 100 }
const session: ThreeCardPokerPlaySession = {
  contractVersion: 'cards.three-card-poker.table.v1', kind: 'table', version: 8,
  table: {
    tableId: 'table-1', phase: 'active', round: 1,
    dealer: { cards: [{ hidden: true }, { hidden: true }, { hidden: true }], rank: null },
    seats: [{ seatId: 'seat-1', displayName: 'Ada', seat: 0, status: 'playing', ante: 5, pairPlus: 0, playWager: 0, totalWager: 5, payout: 0, anteBonusPayout: 0, pairPlusPayout: 0, hand: { cards: [{ rank: 'Q', suit: 'spades', hidden: false }, { rank: '6', suit: 'hearts', hidden: false }, { rank: '4', suit: 'diamonds', hidden: false }], rank: 'high-card' }, isCurrentPlayer: true }],
    activeSeat: 0, legalActions: ['play', 'fold'], createdAtUtc: '2026-10-12T12:00:00Z', updatedAtUtc: '2026-10-12T12:00:00Z',
    actionDeadlineAtUtc: '2026-10-12T12:00:30Z', wagerDeadlineAtUtc: null, transition: null, nextTransitionAtUtc: null,
    remainingActionMilliseconds: 30_000, remainingWagerMilliseconds: 0, remainingTransitionMilliseconds: 0,
  },
}
function successfulFetch(value: unknown) { return vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } })) }

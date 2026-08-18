import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  SolitaireRequestError,
  getSolitaireSession,
  joinSolitaireQueue,
  postCommandWithReconciliation,
  postSolitaireCommand,
  stableSolitaireMutation,
} from './solitaireApi'
import type {
  SolitaireMatchSession,
  SolitaireMutationResponse,
  SolitairePlayerStatus,
} from './solitaireTypes'

describe('competitive Solitaire API boundary', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('sends only a typed move and ExpectedVersion with its idempotency header', async () => {
    const fetchMock = successfulFetch(idleMutation)
    vi.stubGlobal('fetch', fetchMock)

    await postSolitaireCommand('a'.repeat(64), {
      type: 'move',
      expectedVersion: 7,
      from: { zone: 'tableau', index: 3 },
      startIndex: 4,
      to: { zone: 'foundation', index: 2 },
    }, 'move_request_00000001')

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    const headers = new Headers(init.headers)
    const body = JSON.parse(String(init.body)) as Record<string, unknown>
    expect(path).toBe(`/api/solitaire/matches/${'a'.repeat(64)}/commands`)
    expect(init.method).toBe('POST')
    expect(headers.get('Idempotency-Key')).toBe('move_request_00000001')
    expect(body).toEqual({
      type: 'move',
      expectedVersion: 7,
      from: { zone: 'tableau', index: 3 },
      startIndex: 4,
      to: { zone: 'foundation', index: 2 },
    })
    for (const forbidden of ['score', 'time', 'elapsedSeconds', 'seed', 'state', 'completion', 'payout', 'balance']) {
      expect(body).not.toHaveProperty(forbidden)
    }
  })

  it('uses the same stable key in the queue body and Idempotency-Key header', async () => {
    const fetchMock = successfulFetch(idleMutation)
    vi.stubGlobal('fetch', fetchMock)

    await joinSolitaireQueue(6, 10, 3, 'queue_request_0000001')

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('queue_request_0000001')
    expect(JSON.parse(String(init.body))).toEqual({
      playerCount: 6,
      buyInCredits: 10,
      drawCount: 3,
      idempotencyKey: 'queue_request_0000001',
    })
  })

  it('reuses an uncertain request key for the same mutation fingerprint', () => {
    const first = stableSolitaireMutation(null, 'command:match:3:draw')
    const retry = stableSolitaireMutation(first, 'command:match:3:draw')

    expect(retry).toBe(first)
    expect(retry.idempotencyKey).toBe(first.idempotencyKey)
    expect(retry.idempotencyKey.length).toBeGreaterThanOrEqual(16)
  })

  it('reconciles a stale command from a fresh authoritative snapshot', async () => {
    const refreshed = matchSession(8)
    const command = vi.fn().mockRejectedValue(new SolitaireRequestError(
      'Version changed.',
      409,
      { code: 'solitaire-state-conflict' },
    ))
    const session = vi.fn().mockResolvedValue(refreshed)

    const outcome = await postCommandWithReconciliation(
      matchSession(7),
      { type: 'draw' },
      'command_request_0001',
      { command, session },
    )

    expect(command).toHaveBeenCalledWith(
      refreshed.matchId,
      { type: 'draw', expectedVersion: 7 },
      'command_request_0001',
    )
    expect(session).toHaveBeenCalledOnce()
    expect(outcome).toEqual({ session: refreshed, mutation: null, reconciled: true })
  })

  it('surfaces the server feature-gate code without enabling a queue', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      code: 'competitive-solitaire-disabled',
      error: 'Competitive Solitaire is still being verified and cannot accept a buy-in yet.',
    }), { status: 503, headers: { 'Content-Type': 'application/json' } })))

    await expect(getSolitaireSession()).rejects.toMatchObject({
      name: 'SolitaireRequestError',
      status: 503,
      code: 'competitive-solitaire-disabled',
    })
  })

  it('accepts a redacted authoritative match with full nested mutation fields', async () => {
    const match = matchSession(9)
    const response: SolitaireMutationResponse = { session: match, balanceCredits: 95 }
    const fetchMock = successfulFetch(response)
    vi.stubGlobal('fetch', fetchMock)

    const result = await postSolitaireCommand(match.matchId, {
      type: 'draw',
      expectedVersion: 8,
    }, 'redacted_match_0001')

    expect(result.session).toEqual(match)
    expect(result.session).toMatchObject({ kind: 'match', version: 9, score: 0, moves: 0 })
    expect(match.game.stock[0]).toEqual({ isFaceUp: false })
    expect(match.game.tableau[0][1]).toEqual({
      isFaceUp: true,
      id: 'hearts-1',
      suit: 'hearts',
      rank: 1,
    })
    expect(JSON.stringify(result)).not.toMatch(/"(?:dealSeed|seed)"/i)
  })

  it('accepts a safe persisted integrity warning without exposing internal state', async () => {
    const match = {
      ...matchSession(10),
      integrityWarning: {
        warningId: 'warning-1234567890abcdef',
        reason: 'That action was not legal from the last verified board position.',
        purpose: 'This warning protects fair competitive play.',
        occurredAtUtc: '2026-08-17T12:00:00Z',
        acknowledged: false,
      },
    }
    vi.stubGlobal('fetch', successfulFetch(match))

    await expect(getSolitaireSession()).resolves.toMatchObject({
      kind: 'match',
      integrityWarning: { warningId: 'warning-1234567890abcdef', acknowledged: false },
    })
    expect(JSON.stringify(match.integrityWarning)).not.toMatch(/seed|deck|expectedVersion|idempotency/i)
  })

  it('accepts exact-seat matches with public open seats and integrity status', async () => {
    const match = matchSession(9)
    const response = {
      ...match,
      players: [
        ...match.players.slice(0, 2),
        {
          playerId: 'open-seat-3',
          displayName: 'Open seat',
          seat: 3,
          joinedAtUtc: '1970-01-01T00:00:00Z',
          status: 'open',
          isCurrentPlayer: false,
        },
        { ...match.players[3], status: 'integrity-failed' },
      ],
    }
    vi.stubGlobal('fetch', successfulFetch(response))

    await expect(getSolitaireSession()).resolves.toMatchObject({
      kind: 'match',
      players: expect.arrayContaining([
        expect.objectContaining({ playerId: 'open-seat-3', status: 'open' }),
        expect.objectContaining({ status: 'integrity-failed' }),
      ]),
    })
  })

  it('rejects malformed public open-seat identities', async () => {
    const match = matchSession(9)
    vi.stubGlobal('fetch', successfulFetch({
      ...match,
      players: match.players.map((player, index) => index === 2
        ? { ...player, status: 'open', displayName: 'Computer' }
        : player),
    }))

    await expect(getSolitaireSession()).rejects.toThrow('invalid solitaire session')
  })

  it.each([
    ['hidden card identity', () => {
      const match = matchSession(3) as unknown as Record<string, any>
      match.game.stock[0] = { isFaceUp: false, id: 'spades-13', suit: 'spades', rank: 13 }
      return match
    }],
    ['match seed', () => ({ ...matchSession(3), dealSeed: 42 })],
    ['game seed', () => ({
      ...matchSession(3),
      game: { ...matchSession(3).game, seed: 42 },
    })],
    ['unknown player status', () => ({
      ...matchSession(3),
      players: matchSession(3).players.map((player, index) => index === 3
        ? { ...player, status: 'future-paused' }
        : player),
    })],
  ])('rejects a response containing %s', async (_label, responseFactory) => {
    vi.stubGlobal('fetch', successfulFetch(responseFactory()))

    await expect(getSolitaireSession()).rejects.toThrow(
      'The server returned an invalid solitaire session response.',
    )
  })
})

const idleMutation: SolitaireMutationResponse = {
  session: { kind: 'idle' },
  balanceCredits: 125,
}

function successfulFetch(value: unknown) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }))
}

function matchSession(version: number): SolitaireMatchSession {
  return {
    kind: 'match',
    matchId: 'b'.repeat(64),
    playerCount: 4,
    buyInCredits: 5,
    prizePoolCredits: 20,
    winnerPayoutCredits: 18,
    startedAtUtc: '2026-08-14T00:00:00Z',
    deadlineAtUtc: '2026-08-14T00:10:00Z',
    version,
    score: 0,
    moves: 0,
    remainingMilliseconds: 600_000,
    isPaused: false,
    pauseRemainingMilliseconds: 600_000,
    canUndo: false,
    game: {
      stock: [{ isFaceUp: false }],
      waste: [],
      foundations: [[], [], [], []],
      tableau: [[
        { isFaceUp: false },
        { isFaceUp: true, id: 'hearts-1', suit: 'hearts', rank: 1 },
      ], [], [], [], [], [], []],
      drawCount: 1,
      score: 0,
      moves: 0,
      message: 'Your move',
    },
    players: [
      solitairePlayer('player-1', 'Ada', 0, 'playing', true),
      solitairePlayer('player-2', 'Grace', 1, 'playing', false),
      solitairePlayer('player-3', 'Linus', 2, 'finished', false),
      solitairePlayer('player-4', 'Margaret', 3, 'playing', false),
    ],
  }
}

function solitairePlayer(
  playerId: string,
  displayName: string,
  seat: number,
  status: SolitairePlayerStatus,
  isCurrentPlayer: boolean,
) {
  return {
    playerId,
    displayName,
    seat,
    joinedAtUtc: '2026-08-14T00:00:00Z',
    status,
    isCurrentPlayer,
  }
}

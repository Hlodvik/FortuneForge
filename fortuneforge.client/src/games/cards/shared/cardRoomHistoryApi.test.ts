import { afterEach, describe, expect, it, vi } from 'vitest'
import { getCardRoomHistory, markCardRoomResultSeen } from './cardRoomHistoryApi'

describe('card-room history API', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('accepts sanitized paid and claimable result summaries', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([
      {
        resultId: 'result_0000000001',
        game: 'solitaire',
        mode: 'competitive',
        matchId: 'match_00000000001',
        startedAtUtc: '2026-08-16T12:00:00Z',
        completedAtUtc: '2026-08-16T12:10:00Z',
        unseen: true,
        requiresClaim: true,
        winningsCredits: 9,
        wagerCredits: 5,
        netCredits: 4,
        score: 820,
        moves: 74,
        schemaVersion: 1,
      },
    ]), { status: 200, headers: { 'Content-Type': 'application/json' } })))

    const history = await getCardRoomHistory()
    expect(history).toHaveLength(1)
    expect(history[0]).toMatchObject({ game: 'solitaire', unseen: true, requiresClaim: true })
  })

  it('marks an already-paid result seen without sending financial fields', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('crypto', { randomUUID: () => 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee' })

    await markCardRoomResultSeen('result_0000000001')

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cards/history/result_0000000001/seen')
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('aaaaaaaabbbb4ccc8dddeeeeeeeeeeee')
  })

  it('accepts settled Three Card Poker rounds in the shared history', async () => {
    const result = { resultId: 'three-card-round-1', game: 'three-card-poker', mode: 'credit-table', matchId: 'table-1', startedAtUtc: '2026-10-12T12:00:00Z', completedAtUtc: '2026-10-12T12:01:00Z', unseen: true, requiresClaim: false, winningsCredits: 20, wagerCredits: 10, netCredits: 10, score: null, moves: null, schemaVersion: 1 }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([result]), { status: 200 })))
    await expect(getCardRoomHistory()).resolves.toEqual([result])
  })

  it('accepts settled Hi-Lo summaries with exact fractional money and no claim', async () => {
    const result = { resultId: 'hi-lo-round-0001', game: 'hi-lo', mode: 'credit', matchId: 'round-1', startedAtUtc: '2026-10-23T12:00:00Z', completedAtUtc: '2026-10-23T12:01:00Z', unseen: true, requiresClaim: false, winningsCredits: 2.14, wagerCredits: 1, netCredits: 1.14, score: null, moves: null, schemaVersion: 1 }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([result]), { status: 200 })))
    await expect(getCardRoomHistory()).resolves.toEqual([result])
  })
})

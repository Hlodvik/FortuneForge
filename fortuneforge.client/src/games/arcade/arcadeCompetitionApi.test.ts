import { describe, expect, it, vi } from 'vitest'
import { ArcadeCompetitionRequestError, HttpArcadeCompetitionGateway } from './arcadeCompetitionApi'
import type { ArcadeCompetitionPaidPeriod } from './arcadeCompetitionApi'
import type { AsteroidsReplayPayload } from '@fortuneforge/games-asteroids'
import type { FlappyReplayPayload } from '@fortuneforge/games-flappy'

const replay: AsteroidsReplayPayload = { totalSteps: 4, commands: [{ step: 0, input: 8 }] }
const flappyReplay: FlappyReplayPayload = { totalTicks: 36, flapTicks: [] }
const runId = 'asteroids_0123456789abcdef'

describe('Asteroids paid competition transport', () => {
  it('posts an empty paid-attempt request with the caller idempotency key and parses seed hex losslessly', async () => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse({
      attemptId: 'attempt-1', gameId: 'asteroids', period: 'daily', startsAtUtc: '2026-09-03T22:00:00Z', endsAtUtc: '2026-09-04T22:00:00Z', entryFeeCents: 100, wasReplay: false, runId, seedHex: 'ffffffffffffffff',
    }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)

    const result = await gateway.startAsteroidsAttempt('daily', 'attempt-key-1')

    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/arcade-competitions/asteroids/daily/attempts')
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('attempt-key-1')
    expect(init.body).toBeUndefined()
    expect(result.seedHex).toBe('ffffffffffffffff')
  })

  it('posts only replay transitions to the requested run path', async () => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse({ runId, score: 100, terminal: 'completed', wasReplay: false }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)

    await gateway.completeAsteroidsReplay('weekly', runId, replay)

    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`/api/arcade-competitions/asteroids/weekly/runs/${runId}/replay`)
    expect(init.method).toBe('POST')
    expect(JSON.parse(String(init.body))).toEqual(replay)
    expect(String(init.body)).not.toMatch(/score|user|seed|time/i)
  })

  it('preserves stable problem status and codes, and rejects malformed successful payloads', async () => {
    const gateway = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'arcade-asteroids-run-conflict' }), { status: 409 })))
    await expect(gateway.completeAsteroidsReplay('daily', runId, replay)).rejects.toMatchObject({ name: ArcadeCompetitionRequestError.name, status: 409, code: 'arcade-asteroids-run-conflict' })

    const malformed = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(jsonResponse({ runId, score: 1.5, terminal: 'completed', wasReplay: false })))
    await expect(malformed.completeAsteroidsReplay('daily', runId, replay)).rejects.toThrow('invalid arcade competition response')
  })

  it('rejects successful payloads that do not bind to the requested period or run', async () => {
    const wrongPeriod = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(jsonResponse({
      attemptId: 'attempt-1', gameId: 'asteroids', period: 'weekly', startsAtUtc: '2026-09-03T22:00:00Z', endsAtUtc: '2026-09-04T22:00:00Z', entryFeeCents: 100, wasReplay: false, runId, seedHex: '000000000000002a',
    })))
    await expect(wrongPeriod.startAsteroidsAttempt('daily', 'attempt-key-1')).rejects.toThrow('invalid arcade competition response')

    const wrongRun = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(jsonResponse({
      runId: 'asteroids_abcdef0123456789', score: 100, terminal: 'completed', wasReplay: false,
    })))
    await expect(wrongRun.completeAsteroidsReplay('daily', runId, replay)).rejects.toThrow('invalid arcade competition response')
  })

  it('excludes all-time from the paid period type', () => {
    const period: ArcadeCompetitionPaidPeriod = 'weekly'
    expect(period).toBe('weekly')
    // @ts-expect-error all-time is read-only and cannot be supplied to paid transport.
    const invalid: ArcadeCompetitionPaidPeriod = 'all-time'
    expect(invalid).toBe('all-time')
  })
})

describe('Asteroids authenticated free-run transport', () => {
  it('starts a persisted run with an idempotency key and accepts only a server identity and seed', async () => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse({
      runId: 'asteroids_free_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
      seedHex: '000000000000002a',
      startedAtUtc: '2026-09-05T12:00:00+00:00',
      wasReplay: false,
    }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)

    const result = await gateway.startFreeAsteroidsRun('asteroids-0123456789abcdef')

    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/arcade-competitions/asteroids/free/runs')
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('asteroids-0123456789abcdef')
    expect(init.body).toBeUndefined()
    expect(result.seedHex).toBe('000000000000002a')
  })

  it('submits only the exact free replay and validates that completion remains bound to the run', async () => {
    const freeRunId = 'asteroids_free_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
    const fetcher = vi.fn().mockResolvedValue(jsonResponse({ runId: freeRunId, score: 100, terminal: 'completed', wasReplay: true }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)

    const result = await gateway.completeFreeAsteroidsReplay(freeRunId, replay)

    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`/api/arcade-competitions/asteroids/free/runs/${freeRunId}/replay`)
    expect(JSON.parse(String(init.body))).toEqual(replay)
    expect(String(init.body)).not.toMatch(/score|user|seed|time/i)
    expect(result.wasReplay).toBe(true)
  })
})

describe('Flappy authenticated free-run transport', () => {
  it('starts a persisted run with an idempotency key and accepts only a server identity and uint seed', async () => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse({
      runId: 'flappy_free_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
      seed: 4294967295,
      startedAtUtc: '2026-09-06T12:00:00+00:00',
      wasReplay: false,
    }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)

    const result = await gateway.startFreeFlappyRun('flappy-0123456789abcdef')

    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/arcade-competitions/flappy/free/runs')
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('flappy-0123456789abcdef')
    expect(init.body).toBeUndefined()
    expect(result.seed).toBe(4294967295)
  })

  it('submits only flap timing and validates that completion remains bound to the run', async () => {
    const freeRunId = 'flappy_free_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
    const fetcher = vi.fn().mockResolvedValue(jsonResponse({ runId: freeRunId, score: 3, terminal: 'obstacle-collision', wasReplay: true }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)

    const result = await gateway.completeFreeFlappyReplay(freeRunId, flappyReplay)

    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`/api/arcade-competitions/flappy/free/runs/${freeRunId}/replay`)
    expect(JSON.parse(String(init.body))).toEqual(flappyReplay)
    expect(String(init.body)).not.toMatch(/score|user|seed|time/i)
    expect(result.wasReplay).toBe(true)
  })
})

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

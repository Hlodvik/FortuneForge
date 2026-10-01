import { describe, expect, it, vi } from 'vitest'
import type { FlappyReplayPayload } from '@fortuneforge/games-flappy'
import { ArcadeCompetitionRequestError, HttpArcadeCompetitionGateway } from './arcadeCompetitionApi'

const runId = `flappy_free_${'0123456789abcdef'.repeat(4)}`
const otherRunId = `flappy_free_${'fedcba9876543210'.repeat(4)}`
const startKey = 'flappy-start-0123456789abcdef'
const replay: FlappyReplayPayload = { totalTicks: 36, flapTicks: [] }
const start = { runId, seed: 17, startedAtUtc: '2026-10-01T12:00:00Z', wasReplay: false }
const completion = { runId, score: 0, terminal: 'ground-collision', wasReplay: false }

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('Flappy free-run request boundary', () => {
  it.each(['a'.repeat(16), 'A'.repeat(128), startKey, 'Flappy_START_0001'])('accepts a server-valid start key: %s', async key => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse(start))
    await expect(new HttpArcadeCompetitionGateway(fetcher).startFreeFlappyRun(key)).resolves.toEqual(start)
    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/arcade-competitions/flappy/free/runs')
    expect(init).toMatchObject({ method: 'POST', cache: 'no-store' })
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe(key)
    expect(init.body).toBeUndefined()
  })

  it.each([
    ['empty', ''], ['short', 'a'.repeat(15)], ['long', 'a'.repeat(129)],
    ['space', 'flappy start-0001'], ['leading whitespace', ` ${startKey}`],
    ['header break', `${startKey}\n`], ['unicode', 'flappy-start-你好0001'],
    ['punctuation', 'flappy.start.0001'], ['null', null], ['number', 17], ['undefined', undefined],
  ])('rejects an invalid %s start key before any request', async (_label, key) => {
    const fetcher = vi.fn()
    await expect(new HttpArcadeCompetitionGateway(fetcher).startFreeFlappyRun(key as string)).rejects.toThrow('Flappy start request key is invalid')
    expect(fetcher).not.toHaveBeenCalled()
  })

  it.each([
    ['empty', ''], ['old short fixture', 'flappy_free_0123456789abcdef'],
    ['short digest', `flappy_free_${'a'.repeat(63)}`], ['long digest', `flappy_free_${'a'.repeat(65)}`],
    ['uppercase digest', `flappy_free_${'A'.repeat(64)}`], ['wrong game', `asteroids_free_${'a'.repeat(64)}`],
    ['nonhex', `flappy_free_${'g'.repeat(64)}`], ['path suffix', `${runId}/replay`],
    ['number', 17], ['null', null],
  ])('rejects an invalid %s run identity before replay submission', async (_label, identity) => {
    const fetcher = vi.fn()
    await expect(new HttpArcadeCompetitionGateway(fetcher).completeFreeFlappyReplay(identity as string, replay)).rejects.toThrow('Flappy run id is invalid')
    expect(fetcher).not.toHaveBeenCalled()
  })

  it.each([
    ['null', null], ['array', []], ['missing ticks', { flapTicks: [] }],
    ['zero duration', { totalTicks: 0, flapTicks: [] }], ['negative duration', { totalTicks: -1, flapTicks: [] }],
    ['fractional duration', { totalTicks: 1.5, flapTicks: [] }], ['over-limit duration', { totalTicks: 9_001, flapTicks: [] }],
    ['string duration', { totalTicks: '36', flapTicks: [] }], ['missing flaps', { totalTicks: 36 }],
    ['nonarray flaps', { totalTicks: 36, flapTicks: '0' }],
    ['negative flap', { totalTicks: 36, flapTicks: [-1] }], ['flap after terminal tick', { totalTicks: 36, flapTicks: [36] }],
    ['fractional flap', { totalTicks: 36, flapTicks: [1.5] }], ['string flap', { totalTicks: 36, flapTicks: ['1'] }],
    ['duplicate flap', { totalTicks: 36, flapTicks: [1, 1] }], ['unordered flaps', { totalTicks: 36, flapTicks: [2, 1] }],
    ['excessive flaps', { totalTicks: 9_000, flapTicks: Array.from({ length: 1_501 }, (_, index) => index) }],
    ['sparse flaps', { totalTicks: 36, flapTicks: new Array(1) }],
  ])('rejects %s replay input without posting', async (_label, payload) => {
    const fetcher = vi.fn()
    await expect(new HttpArcadeCompetitionGateway(fetcher).completeFreeFlappyReplay(runId, payload as FlappyReplayPayload)).rejects.toThrow('Flappy replay input is invalid')
    expect(fetcher).not.toHaveBeenCalled()
  })

  it.each([
    { totalTicks: 1, flapTicks: [] },
    { totalTicks: 9_000, flapTicks: [0, 8_999] },
    { totalTicks: 9_000, flapTicks: Array.from({ length: 1_500 }, (_, index) => index) },
  ])('passes bounded canonical timing; terminal validity remains server-owned: $totalTicks', async payload => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse(completion))
    await new HttpArcadeCompetitionGateway(fetcher).completeFreeFlappyReplay(runId, payload)
    const [url, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`/api/arcade-competitions/flappy/free/runs/${runId}/replay`)
    expect(init).toMatchObject({ method: 'POST', cache: 'no-store' })
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json')
    expect(JSON.parse(String(init.body))).toEqual(payload)
  })

  it('serializes timing only, excluding runtime outcome, seed and identity fields', async () => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse(completion))
    const payload = { totalTicks: 36, flapTicks: [0], score: 999, seed: 17, userId: 'someone', terminal: 'ceiling-collision' }
    await new HttpArcadeCompetitionGateway(fetcher).completeFreeFlappyReplay(runId, payload)
    const init = fetcher.mock.calls[0]?.[1] as RequestInit
    expect(JSON.parse(String(init.body))).toEqual({ totalTicks: 36, flapTicks: [0] })
  })

  it('keeps a caller-requested retry identical and exposes the server replay flag', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ code: 'arcade-flappy-free-run-failed' }, 500))
      .mockResolvedValueOnce(jsonResponse({ ...completion, wasReplay: true }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)
    await expect(gateway.completeFreeFlappyReplay(runId, replay)).rejects.toMatchObject({ status: 500 })
    expect(fetcher).toHaveBeenCalledTimes(1)
    await expect(gateway.completeFreeFlappyReplay(runId, replay)).resolves.toEqual({ ...completion, wasReplay: true })
    expect(fetcher.mock.calls[0]).toEqual(fetcher.mock.calls[1])
  })

  it('passes AbortSignals to both writes and preserves an actual abort rejection', async () => {
    const controller = new AbortController()
    const fetcher = vi.fn().mockImplementation((_input: RequestInfo | URL, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })
    }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)
    const starting = gateway.startFreeFlappyRun(startKey, controller.signal)
    const submitting = gateway.completeFreeFlappyReplay(runId, replay, controller.signal)
    expect(fetcher.mock.calls.map(call => (call[1] as RequestInit).signal)).toEqual([controller.signal, controller.signal])
    controller.abort()
    await expect(starting).rejects.toMatchObject({ name: 'AbortError' })
    await expect(submitting).rejects.toMatchObject({ name: 'AbortError' })
    expect(fetcher).toHaveBeenCalledTimes(2)
  })
})

describe('Flappy free-run successful response boundary', () => {
  it.each([1, 0xffff_ffff])('accepts an exact uint seed boundary %i', async seed => {
    const gateway = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(jsonResponse({ ...start, seed })))
    await expect(gateway.startFreeFlappyRun(startKey)).resolves.toMatchObject({ seed })
  })

  it.each(['2026-10-01T12:00:00Z', '2026-10-01T12:00:00+00:00', '2026-10-01T12:00:00.1234567Z'])('preserves server UTC timestamp precision: %s', async startedAtUtc => {
    const gateway = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(jsonResponse({ ...start, startedAtUtc })))
    await expect(gateway.startFreeFlappyRun(startKey)).resolves.toMatchObject({ startedAtUtc })
  })

  it.each([
    ['null', null], ['array', []], ['missing fields', { runId }],
    ['wrong identity', { ...start, runId: `asteroids_free_${'a'.repeat(64)}` }],
    ['zero seed', { ...start, seed: 0 }], ['negative seed', { ...start, seed: -1 }],
    ['fractional seed', { ...start, seed: 1.5 }], ['uint overflow', { ...start, seed: 0x1_0000_0000 }],
    ['string seed', { ...start, seed: '17' }], ['missing seed', { ...start, seed: null }],
    ['nonboolean replay', { ...start, wasReplay: 'false' }],
    ['nonUTC timestamp', { ...start, startedAtUtc: '2026-10-01T12:00:00-05:00' }],
    ['date only', { ...start, startedAtUtc: '2026-10-01' }],
    ['impossible date', { ...start, startedAtUtc: '2026-02-31T12:00:00Z' }],
    ['missing timestamp', { ...start, startedAtUtc: null }],
  ])('rejects a malformed %s start response', async (_label, response) => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse(response))
    await expect(new HttpArcadeCompetitionGateway(fetcher).startFreeFlappyRun(startKey)).rejects.toThrow('invalid arcade competition response')
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it.each(['obstacle-collision', 'ground-collision', 'ceiling-collision'])('accepts server terminal %s', async terminal => {
    const gateway = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(jsonResponse({ ...completion, terminal })))
    await expect(gateway.completeFreeFlappyReplay(runId, replay)).resolves.toMatchObject({ terminal })
  })

  it.each([
    ['null', null], ['array', []], ['missing fields', { runId }],
    ['wrong run', { ...completion, runId: otherRunId }], ['wrong game', { ...completion, runId: `asteroids_free_${'a'.repeat(64)}` }],
    ['negative score', { ...completion, score: -1 }], ['fractional score', { ...completion, score: 1.5 }],
    ['unsafe integer score', { ...completion, score: Number.MAX_SAFE_INTEGER + 1 }],
    ['string score', { ...completion, score: '0' }], ['missing score', { ...completion, score: null }],
    ['nonboolean replay', { ...completion, wasReplay: 'false' }],
    ['nonterminal', { ...completion, terminal: 'playing' }], ['unknown terminal', { ...completion, terminal: 'timeout' }],
  ])('rejects a malformed %s completion response', async (_label, response) => {
    const fetcher = vi.fn().mockResolvedValue(jsonResponse(response))
    await expect(new HttpArcadeCompetitionGateway(fetcher).completeFreeFlappyReplay(runId, replay)).rejects.toThrow('invalid arcade competition response')
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('projects canonical public DTO fields and does not expose unexpected response metadata', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ ...start, score: 123, playerId: 'other', account: { slotsCredits: 999 } }))
      .mockResolvedValueOnce(jsonResponse({ ...completion, seed: 999, wallet: { slotsCredits: 999 }, replayDigest: 'private' }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)
    await expect(gateway.startFreeFlappyRun(startKey)).resolves.toEqual(start)
    await expect(gateway.completeFreeFlappyReplay(runId, replay)).resolves.toEqual(completion)
  })

  it('never replaces the authoritative score or terminal with local display assumptions', async () => {
    const authoritative = { ...completion, score: 9, terminal: 'obstacle-collision', wasReplay: true }
    const gateway = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(jsonResponse(authoritative)))
    await expect(gateway.completeFreeFlappyReplay(runId, replay)).resolves.toEqual(authoritative)
  })

  it.each([401, 409, 429, 500])('preserves server status/code %i and does not automatically retry', async status => {
    const code = status === 409 ? 'arcade-flappy-free-run-conflict' : 'arcade-flappy-free-run-failed'
    const fetcher = vi.fn().mockResolvedValue(jsonResponse({ code }, status))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)
    await expect(gateway.completeFreeFlappyReplay(runId, replay)).rejects.toMatchObject({ name: ArcadeCompetitionRequestError.name, status, code })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('treats unreadable successful JSON as uncertainty and retains a non-JSON failure status', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(new Response('not json', { status: 200 }))
      .mockResolvedValueOnce(new Response('service unavailable', { status: 503 }))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)
    await expect(gateway.startFreeFlappyRun(startKey)).rejects.toThrow('invalid arcade competition response')
    await expect(gateway.completeFreeFlappyReplay(runId, replay)).rejects.toMatchObject({ status: 503 })
    expect(fetcher).toHaveBeenCalledTimes(2)
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { completeSolitaireFreeRun, startSolitaireFreeRun } from './solitaireFreeRunApi'

describe('Solitaire free-run API boundary', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('starts with only draw choice and an idempotency key, accepting server identity', async () => {
    const response = {
      runId: `solitaire_free_${'a'.repeat(64)}`,
      seed: 42,
      drawCount: 3,
      startedAtUtc: '2026-09-06T12:00:00Z',
      wasAlreadyStarted: false,
    }
    const fetchMock = successfulFetch(response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(startSolitaireFreeRun(3, 'solitaire_free_start_0001')).resolves.toEqual(response)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/solitaire/free/runs')
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('solitaire_free_start_0001')
    expect(JSON.parse(String(init.body))).toEqual({ drawCount: 3 })
  })

  it('submits only the effective replay and accepts authoritative result fields', async () => {
    const runId = `solitaire_free_${'b'.repeat(64)}`
    const response = {
      runId,
      seed: 73,
      drawCount: 1,
      score: 10,
      moves: 2,
      elapsedMilliseconds: 42_000,
      terminal: 'submitted',
      completedAtUtc: '2026-09-06T12:00:42Z',
      wasAlreadyCompleted: false,
    }
    const fetchMock = successfulFetch(response)
    vi.stubGlobal('fetch', fetchMock)
    const commands = [
      { type: 'draw' as const },
      {
        type: 'move' as const,
        from: { zone: 'waste' as const, index: 0 },
        startIndex: 0,
        to: { zone: 'foundation' as const, index: 0 },
      },
    ]

    await expect(completeSolitaireFreeRun(runId, commands)).resolves.toEqual(response)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe(`/api/solitaire/free/runs/${runId}/replay`)
    expect(JSON.parse(String(init.body))).toEqual({ commands })
    for (const forbidden of ['score', 'moves', 'elapsedMilliseconds', 'terminal', 'seed', 'balance', 'payout']) {
      expect(JSON.parse(String(init.body))).not.toHaveProperty(forbidden)
    }
  })
})

function successfulFetch(value: unknown) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }))
}

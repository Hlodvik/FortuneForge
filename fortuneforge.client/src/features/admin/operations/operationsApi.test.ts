import { afterEach, describe, expect, it, vi } from 'vitest'
import { getOperationsDashboard, OperationsRequestError } from './operationsApi'

describe('operations api', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('uses six GET-only cookie-authenticated requests with UTC ranges', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ items: [], nextCursor: null }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await getOperationsDashboard(24)

    expect(fetchMock).toHaveBeenCalledTimes(6)
    const calls = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>
    for (const [path, init] of calls) {
      expect(path).toMatch(/^\/api\/admin\/operations\/(overview|activity|queues|matches|integrity|bots)\?/)
      expect(path).toContain('from=')
      expect(path).toContain('to=')
      expect(init.method).toBe('GET')
      expect(init.credentials).toBe('include')
      expect(init.body).toBeUndefined()
    }
  })

  it('surfaces sanitized server errors without retrying a read', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ error: 'Administrator access is required.' }), {
      status: 403,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(getOperationsDashboard()).rejects.toEqual(
      new OperationsRequestError('Administrator access is required.', 403),
    )
    expect(fetchMock).toHaveBeenCalledTimes(6)
  })
})

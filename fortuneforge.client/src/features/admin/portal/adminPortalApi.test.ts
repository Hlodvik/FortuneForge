import { afterEach, describe, expect, it, vi } from 'vitest'
import { adminPortalApi, AdminPortalError } from './adminPortalApi'
vi.mock('../../account/services/accountsApi', () => ({ fetchWithAccountSession: (...args: unknown[]) => fetch(...args as [RequestInfo, RequestInit]) }))
afterEach(() => vi.unstubAllGlobals())
describe('admin portal requests', () => {
  it('encodes player IDs/cursors and avoids caching private data', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], nextCursor: null }))); vi.stubGlobal('fetch', fetcher)
    await adminPortalApi.gaming('someone?x=1', 'opaque+/=')
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/players/someone%3Fx%3D1/gaming?limit=25&cursor=opaque%2B%2F%3D')
    expect(fetcher.mock.calls[0][1].cache).toBe('no-store')
  })
  it('sends CSRF intent, idempotency key, reason and expected version on writes', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{}')); vi.stubGlobal('fetch', fetcher)
    await adminPortalApi.change({ kind: 'bots', enabled: false, expectedVersion: 7, reason: 'Maintenance window', idempotencyKey: 'one-logical-attempt' })
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/management/bots')
    expect(fetcher.mock.calls[0][1]).toMatchObject({ method: 'POST', headers: { 'X-FortuneForge-Admin': '1', 'Idempotency-Key': 'one-logical-attempt' } })
    expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ enabled: false, expectedVersion: 7, reason: 'Maintenance window' })
  })
  it('preserves HTTP conflict status and sanitized server message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ error: 'Settings changed' }), { status: 409 })))
    await expect(adminPortalApi.management()).rejects.toMatchObject({ status: 409, message: 'Settings changed' })
  })
  it('does not expose HTML server errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>internal details</html>', { status: 500 })))
    await expect(adminPortalApi.management()).rejects.toBeInstanceOf(AdminPortalError)
    await expect(adminPortalApi.management()).rejects.not.toThrow('internal details')
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { adminGameReportsApi, sendGameReport, type GameReport } from './gameReportsApi'
vi.mock('../account/services/accountsApi', () => ({ fetchWithAccountSession: (...args: unknown[]) => fetch(...args as [RequestInfo, RequestInit]) }))
afterEach(() => vi.unstubAllGlobals())
describe('game report requests', () => {
  it('sends only the chosen category, message and clean game path with retry key and CSRF intent', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'a'.repeat(64), status: 'open', createdAtUtc: '2026-10-24T18:00:00Z' })))
    vi.stubGlobal('fetch', fetcher)
    const signal = new AbortController().signal
    const input = { category: 'bug' as const, message: 'Cards stopped responding after Deal.', path: '/games/andar-bahar' }
    await sendGameReport(input, 'same-logical-report-key', signal)
    expect(fetcher).toHaveBeenCalledWith('/api/game-reports', expect.objectContaining({ method: 'POST', cache: 'no-store', signal, headers: { 'X-FortuneForge-Report': '1', 'Idempotency-Key': 'same-logical-report-key' } }))
    expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual(input)
    expect(fetcher.mock.calls[0][1].body).not.toMatch(/userId|telemetry|screenshot|userAgent/)
  })
  it('keeps raw HTML and internal errors out of player feedback', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>private stack trace</html>', { status: 500 })))
    await expect(sendGameReport({ category: 'other', message: 'A report message', path: '/games/andar-bahar' }, 'retry-report-request')).rejects.toMatchObject({ status: 500, message: expect.stringContaining('could not be confirmed') })
  })
  it.each([{}, null, { id: 'unknown', status: 'open', createdAtUtc: '2026-10-24T18:00:00Z' }, { id: 'a'.repeat(64), status: 'open', createdAtUtc: 'not-a-date' }])('retains uncertain recovery when a successful response has no valid receipt %#', async payload => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(payload))))
    await expect(sendGameReport({ category: 'bug', message: 'A detailed report message', path: '/games/andar-bahar' }, 'same-logical-report-key')).rejects.toMatchObject({ status: 0, message: expect.stringContaining('could not be confirmed') })
  })
  it('encodes admin report references/cursors and uses server revisions for status changes', async () => {
    const fetcher = vi.fn().mockImplementation(async () => new Response('{}')); vi.stubGlobal('fetch', fetcher)
    await adminGameReportsApi.list('opaque+/='); await adminGameReportsApi.detail('id?other')
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/game-reports?limit=25&cursor=opaque%2B%2F%3D')
    expect(fetcher.mock.calls[1][0]).toBe('/api/admin/game-reports/id%3Fother')
    await adminGameReportsApi.update({ id: 'report-one', version: 3 } as GameReport, 'resolved', 'Fixed the layout.')
    expect(fetcher.mock.calls[2][1]).toMatchObject({ method: 'POST', headers: { 'X-FortuneForge-Admin': '1' } })
    expect(JSON.parse(fetcher.mock.calls[2][1].body)).toEqual({ status: 'resolved', expectedVersion: 3, note: 'Fixed the layout.' })
  })
})

// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { attachAuthenticatedFrameTransport } from './authenticatedFrameTransport'

const channel = 'fortune-forge.game-transport.v1'
let detach: () => void
let source: Window
let post: ReturnType<typeof vi.fn>
let fetch: ReturnType<typeof vi.fn<(path: string, init?: RequestInit) => Promise<Response>>>
let account: ReturnType<typeof vi.fn<() => Promise<{ userId: string; balances: { slotsCredits: number } }>>>
beforeEach(() => {
  post = vi.fn(); source = { postMessage: post } as unknown as Window
  fetch = vi.fn(async () => new Response('{"opaque":"game-owned-body"}', { status: 201, headers: { 'X-Test': 'kept' } }))
  account = vi.fn(async () => ({ userId: 'bound-player', balances: { slotsCredits: 100 } }))
  detach = attachAuthenticatedFrameTransport({ accountId: 'bound-player', source: () => source, origin: 'https://game.example.test', authenticatedFetch: fetch, loadAccount: account })
})
afterEach(() => detach())
async function request(method: string, payload: unknown, id = 'rpc-1', origin = 'https://game.example.test') {
  window.dispatchEvent(new MessageEvent('message', { origin, source, data: { channel, id, method, payload } }))
  await vi.waitFor(() => expect(post).toHaveBeenCalled())
  return post.mock.calls.at(-1)![0]
}
it('carries raw authenticated HTTP and deduplicates a transport request without interpreting game fields', async () => {
  const payload = { path: '/api/slots/prepared-spins', init: { method: 'POST', headers: { 'Idempotency-Key': 'same-financial-operation' }, body: '{"opaque":"intent"}' } }
  const response = await request('request', payload)
  expect(response.result).toMatchObject({ status: 201, body: '{"opaque":"game-owned-body"}' })
  expect(new Headers(fetch.mock.calls[0][1]!.headers).get('X-FortuneForge-Expected-Account')).toBe('bound-player')
  await request('request', payload)
  expect(fetch).toHaveBeenCalledOnce()
})
it('rejects game commands and external URLs rather than owning any gameplay adapter', async () => {
  expect((await request('spin', { wagerPoints: 20 })).error).toContain('Invalid')
  expect((await request('request', { path: 'https://elsewhere.test/api/spins', init: { method: 'POST' } }, 'rpc-2')).error).toContain('Invalid')
  expect(fetch).not.toHaveBeenCalled()
})
it('ignores forged origins and refuses account switching', async () => {
  window.dispatchEvent(new MessageEvent('message', { origin: 'https://forged.test', source, data: { channel, id: 'fake', method: 'account' } }))
  expect(account).not.toHaveBeenCalled(); expect(post).not.toHaveBeenCalled()
  account.mockResolvedValue({ userId: 'another-player', balances: { slotsCredits: 100 } })
  expect((await request('account', {})).error).toContain('account changed')
})

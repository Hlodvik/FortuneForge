// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchWithAccountSession, clearAccountToken } from './accountsApi'
import { ACCOUNT_EXPERIENCE_INVALIDATION_KEY, getAccountSessionRevision, subscribeAccountSessionEvents } from './accountSessionEvents'
const cleanups: (() => void)[] = []
beforeEach(() => { sessionStorage.clear(); localStorage.clear() })
afterEach(() => { cleanups.splice(0).forEach(fn => fn()); vi.unstubAllGlobals() })
describe('shared account read invalidation signals', () => {
  it('announces a cookie-only session change from an authentication page with no game consumer', () => {
    clearAccountToken()
    expect(localStorage.getItem(ACCOUNT_EXPERIENCE_INVALIDATION_KEY)).toMatch(/^session-changed:/)
    expect(localStorage.getItem(ACCOUNT_EXPERIENCE_INVALIDATION_KEY)).not.toMatch(/userId|token|hasPlayed/)
  })
  it('invalidates only after a successful mutation, never by trusting a client flag', async () => {
    const listener = vi.fn(); cleanups.push(subscribeAccountSessionEvents(listener))
    const transport = vi.fn().mockResolvedValueOnce(new Response('{}', { status: 400 }))
      .mockResolvedValueOnce(new Response('{}', { status: 200 })).mockResolvedValueOnce(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', transport)
    await fetchWithAccountSession('/api/games/example', { method: 'POST', body: '{"hasPlayed":true}' })
    await fetchWithAccountSession('/api/games/example', { method: 'GET' })
    expect(listener).not.toHaveBeenCalled()
    await fetchWithAccountSession('/api/games/example', { method: 'POST' })
    expect(listener.mock.calls).toEqual([['accepted-mutation']])
  })
  it('changes the session revision without exposing credentials and isolates observer errors', async () => {
    const revision = getAccountSessionRevision(), observer = vi.fn()
    cleanups.push(subscribeAccountSessionEvents(() => { throw new Error('observer failed') }))
    cleanups.push(subscribeAccountSessionEvents(observer))
    expect(() => clearAccountToken()).not.toThrow()
    expect(getAccountSessionRevision()).toBe(revision + 1)
    expect(observer).toHaveBeenCalledWith('session-changed')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 200 })))
    expect((await fetchWithAccountSession('/api/games/example', { method: 'POST' })).ok).toBe(true)
  })

  it('preserves bearer/cookie retry behavior and signals an accepted mutation exactly once', async () => {
    const listener = vi.fn(); cleanups.push(subscribeAccountSessionEvents(listener))
    sessionStorage.setItem('fortune-forge.account-token', 'a'.repeat(32))
    localStorage.setItem('fortune-forge.account-token', 'b'.repeat(32))
    const transport = vi.fn().mockResolvedValueOnce(new Response('{}', { status: 401 }))
      .mockResolvedValueOnce(new Response('{}', { status: 401 })).mockResolvedValueOnce(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', transport)
    const result = await fetchWithAccountSession('/api/games/example', { method: 'POST' })
    expect(result.ok).toBe(true)
    expect(transport).toHaveBeenCalledTimes(3)
    expect(transport.mock.calls.map(([, options]) => (options.headers as Headers).get('Authorization'))).toEqual([
      `Bearer ${'a'.repeat(32)}`, `Bearer ${'b'.repeat(32)}`, null,
    ])
    expect(listener.mock.calls.filter(([event]) => event === 'accepted-mutation')).toHaveLength(1)
    expect(listener).toHaveBeenCalledWith('session-changed')
    expect(sessionStorage.getItem('fortune-forge.account-token')).toBeNull()
  })
})

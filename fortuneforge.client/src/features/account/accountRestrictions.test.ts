// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearAccountRestriction, getAccountRestriction, observeRestrictionResponse, publishAccountRestriction } from './accountRestrictions'
import { fetchWithAccountSession, getAccountToken, getCurrentAccount, loginAccount } from './services/accountsApi'

const response = (body: unknown, status = 403) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
beforeEach(() => { clearAccountRestriction(); sessionStorage.clear(); localStorage.clear() })
afterEach(() => { clearAccountRestriction(); vi.unstubAllGlobals() })

describe('server-account restriction responses', () => {
  it('preserves the original error response and recognizes only explicit restriction codes', async () => {
    const original = response({ code: 'game_restricted', error: 'This game is awaiting review.', gameId: 'mines' })
    expect(await observeRestrictionResponse(original)).toBe(original)
    expect(await original.json()).toEqual({ code: 'game_restricted', error: 'This game is awaiting review.', gameId: 'mines' })
    expect(getAccountRestriction()).toEqual({ code: 'game_restricted', message: 'This game is awaiting review.', gameId: 'mines' })
    clearAccountRestriction()
    for (const value of [response({ code: 'permission_denied' }), response({ code: 'account_restricted' }, 400), new Response('invalid', { status: 403 })])
      await observeRestrictionResponse(value)
    expect(getAccountRestriction()).toBeNull()
  })
  it('cannot downgrade a full account lock to a game or withdrawal notice', () => {
    publishAccountRestriction({ code: 'account_restricted', message: 'Review pending' })
    publishAccountRestriction({ code: 'withdrawals_restricted', message: 'Withdrawal unavailable' })
    expect(getAccountRestriction()?.code).toBe('account_restricted')
  })
  it('observes authenticated cookie responses and exposes the friendly server error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ code: 'account_restricted', error: 'Your account is under review.' })))
    await expect(getCurrentAccount()).rejects.toThrow('Your account is under review.')
    expect(getAccountRestriction()?.code).toBe('account_restricted')
  })
  it('keeps a restricted login authenticated without needing an account or wallet payload', async () => {
    const token = 'a'.repeat(48)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ account: null, token, expiresAtUtc: '2026-11-01T00:00:00Z', restriction: { code: 'account_restricted', reviewPath: '/account/review' } }, 200)))
    const result = await loginAccount({ email: 'user@example.com', password: 'example-password', remainLoggedIn: false })
    expect(result.account).toBeNull(); expect(getAccountToken()).toBe(token)
    expect(getAccountRestriction()?.code).toBe('account_restricted')
  })
  it('does not show a withdrawal warning during ordinary account requests', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response({ accountRestricted: false, reviewStatus: 'clear', message: 'No account restriction.' }, 200))
      .mockResolvedValueOnce(response({ code: 'withdrawals_restricted', error: 'Withdrawals are pending review.' })))
    await fetchWithAccountSession('/api/account/review'); expect(getAccountRestriction()).toBeNull()
    await fetchWithAccountSession('/api/withdrawals', { method: 'POST' })
    expect(getAccountRestriction()?.code).toBe('withdrawals_restricted')
  })
})

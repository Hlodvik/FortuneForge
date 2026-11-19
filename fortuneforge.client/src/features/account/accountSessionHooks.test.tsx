// @vitest-environment jsdom
// Edited: 2026-11-18T22:45:13-06:00 | Computer: Vinnabok
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountRequestError, type AccountSummary } from './services/accountsApi'
import { publishAccountSessionEvent } from './services/accountSessionEvents'
import { useAuthenticatedAccount } from './useAuthenticatedAccount'
import { useOptionalAccountSession } from './useOptionalAccountSession'

const requests = vi.hoisted(() => ({ account: vi.fn(), token: vi.fn(), clear: vi.fn(), replace: vi.fn() }))
vi.mock('./services/accountsApi', async importOriginal => ({
  ...await importOriginal<typeof import('./services/accountsApi')>(),
  getCurrentAccount: requests.account, getAccountToken: requests.token, clearAccountToken: requests.clear,
}))

const account = (userId: string): AccountSummary => ({
  userId, playerName: userId, email: `${userId}@example.test`, role: 'player', createdAtUtc: '2026-11-18T00:00:00Z',
  balances: { slotsCredits: 100, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
})

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function OptionalProbe() {
  const state = useOptionalAccountSession()
  return <output data-status={state.status}>{state.account?.userId ?? state.error ?? 'anonymous'}</output>
}

function ProtectedProbe({ path, publicHome = false }: { path: string; publicHome?: boolean }) {
  const state = useAuthenticatedAccount(path, publicHome ? { unauthenticatedRedirect: '/' } : undefined)
  return <output data-loading={state.isLoading}>{state.account?.userId ?? state.error ?? 'anonymous'}</output>
}

let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  requests.account.mockReset(); requests.token.mockReset().mockReturnValue(null)
  requests.clear.mockReset(); requests.replace.mockReset()
  const originalWindow = window
  const location = new Proxy({}, { get(_target, property) {
    if (property === 'replace') return requests.replace
    const value = Reflect.get(originalWindow.location, property, originalWindow.location)
    return typeof value === 'function' ? value.bind(originalWindow.location) : value
  } })
  vi.stubGlobal('window', new Proxy(originalWindow, { get(target, property) {
    if (property === 'location') return location
    const value = Reflect.get(target, property, target)
    return typeof value === 'function' ? value.bind(target) : value
  } }))
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(async () => {
  await act(async () => root.unmount())
  host.remove(); vi.unstubAllGlobals()
})

describe('account session resolution', () => {
  it('keeps one account request owner and exposes a verified account', async () => {
    requests.account.mockResolvedValue(account('current'))
    await act(async () => root.render(<ProtectedProbe path="/home" publicHome />))
    expect(requests.account).toHaveBeenCalledTimes(1)
    expect(host.textContent).toBe('current')
    expect(requests.replace).not.toHaveBeenCalled()
  })

  it('takes signed-out home visits to the public landing page', async () => {
    requests.account.mockRejectedValue(new AccountRequestError('Sign in required.', 401))
    await act(async () => root.render(<ProtectedProbe path="/home?category=Slots" publicHome />))
    expect(requests.replace).toHaveBeenCalledExactlyOnceWith('/')
    expect(host.querySelector('output')?.getAttribute('data-loading')).toBe('true')
    expect(requests.clear).not.toHaveBeenCalled()
  })

  it.each(['/home/history', '/games/mines'])('keeps the login return path for protected %s', async path => {
    requests.account.mockRejectedValue(new AccountRequestError('Sign in required.', 401))
    await act(async () => root.render(<ProtectedProbe path={path} />))
    expect(requests.replace).toHaveBeenCalledExactlyOnceWith(`/login?returnTo=${encodeURIComponent(path)}`)
  })

  it('does not let an old account response overwrite a changed session', async () => {
    const previous = deferred<AccountSummary>(), current = deferred<AccountSummary>()
    requests.account.mockReturnValueOnce(previous.promise).mockReturnValueOnce(current.promise)
    await act(async () => root.render(<OptionalProbe />))
    await act(async () => publishAccountSessionEvent('session-changed'))
    await act(async () => previous.resolve(account('previous')))
    expect(host.querySelector('output')?.getAttribute('data-status')).toBe('loading')
    expect(host.textContent).not.toContain('previous')
    await act(async () => current.resolve(account('current')))
    expect(host.textContent).toBe('current')
  })

  it('ignores an obsolete 401 without clearing or redirecting the new session', async () => {
    const previous = deferred<AccountSummary>(), current = deferred<AccountSummary>()
    requests.account.mockReturnValueOnce(previous.promise).mockReturnValueOnce(current.promise)
    await act(async () => root.render(<ProtectedProbe path="/home" publicHome />))
    await act(async () => publishAccountSessionEvent('session-changed'))
    await act(async () => current.resolve(account('current')))
    await act(async () => previous.reject(new AccountRequestError('Old session expired.', 401)))
    expect(host.textContent).toBe('current')
    expect(requests.clear).not.toHaveBeenCalled()
    expect(requests.replace).not.toHaveBeenCalled()
  })

  it('removes a rejected local credential once without a cookie-only reload loop', async () => {
    requests.account.mockRejectedValue(new AccountRequestError('Sign in required.', 401))
    requests.token.mockReturnValue('old-token')
    requests.clear.mockImplementation(() => {
      requests.token.mockReturnValue(null)
      publishAccountSessionEvent('session-changed')
    })
    await act(async () => root.render(<ProtectedProbe path="/home" publicHome />))
    expect(requests.clear).toHaveBeenCalledTimes(1)
    expect(requests.account).toHaveBeenCalledTimes(2)
    expect(requests.replace).toHaveBeenCalledExactlyOnceWith('/')
  })

  it('clears a previously shown account while rechecking a changed session', async () => {
    const next = deferred<AccountSummary>()
    requests.account.mockResolvedValueOnce(account('previous')).mockReturnValueOnce(next.promise)
    await act(async () => root.render(<OptionalProbe />))
    expect(host.textContent).toBe('previous')
    await act(async () => publishAccountSessionEvent('session-changed'))
    expect(host.textContent).toBe('anonymous')
    await act(async () => next.reject(new AccountRequestError('Signed out.', 401)))
    expect(host.querySelector('output')?.getAttribute('data-status')).toBe('unauthenticated')
    expect(host.textContent).not.toContain('previous')
  })

  it('keeps request failures separate from a confirmed signed-out session', async () => {
    requests.account.mockRejectedValue(new Error('Account service unavailable.'))
    await act(async () => root.render(<ProtectedProbe path="/home" publicHome />))
    expect(host.textContent).toBe('Account service unavailable.')
    expect(requests.replace).not.toHaveBeenCalled()
  })
})

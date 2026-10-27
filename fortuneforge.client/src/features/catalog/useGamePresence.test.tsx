// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
const mocks = vi.hoisted(() => ({ fetch: vi.fn(), account: vi.fn() }))
vi.mock('../account/services/accountsApi', () => ({ fetchWithAccountSession: mocks.fetch, getCurrentAccount: mocks.account }))
import { presenceGameId, useGamePresence } from './useGamePresence'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { HOME_GAME_CATALOG } from '../../pages/account/homeGameCatalog'

let root: Root | undefined
let visible = true
let focused = true
function Probe({ path }: { path: string }) { useGamePresence(path); return null }
const signals = () => mocks.fetch.mock.calls.map(([, init]) => JSON.parse(init.body as string))
async function render(path: string) {
  root ??= createRoot(document.createElement('div'))
  await act(async () => root!.render(<Probe path={path} />))
}
beforeEach(() => {
  vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', 'true')
  vi.useFakeTimers(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  visible = true; focused = true
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visible ? 'visible' : 'hidden')
  vi.spyOn(document, 'hasFocus').mockImplementation(() => focused)
  mocks.fetch.mockReset().mockResolvedValue(new Response(null, { status: 204 }))
  mocks.account.mockReset().mockResolvedValue({ userId: 'alice' })
})
afterEach(async () => {
  if (root) await act(async () => root!.unmount())
  root = undefined; vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.unstubAllEnvs()
})

describe('foreground game presence', () => {
  it('does not authenticate, send heartbeats or start polling while the API is disabled', async () => {
    vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', undefined)
    await render('/cards/hearts')
    await act(async () => { window.dispatchEvent(new Event('focus')); await vi.advanceTimersByTimeAsync(100_000) })
    await render('/home')
    expect(mocks.account).not.toHaveBeenCalled(); expect(mocks.fetch).not.toHaveBeenCalled()
  })
  it('maps all 40 real game routes and rejects catalogue, demo and invented routes', () => {
    expect(HOME_GAME_CATALOG).toHaveLength(40)
    for (const game of HOME_GAME_CATALOG) expect(presenceGameId(game.href)).toBe(game.id)
    for (const game of SLOT_ROUTE_DEFINITIONS) expect(presenceGameId(game.demoPath)).toBeUndefined()
    for (const route of ['/home', '/cards', '/slots', '/games', '/games/unknown', '/demo/cards/blackjack', '/cards/hearts/rules'])
      expect(presenceGameId(route)).toBeUndefined()
  })
  it('does not request an account or track time outside a playable route', async () => {
    await render('/home'); await act(async () => vi.advanceTimersByTimeAsync(60_000))
    expect(mocks.account).not.toHaveBeenCalled(); expect(mocks.fetch).not.toHaveBeenCalled()
  })
  it('requires an authenticated account and ignores account responses after leaving', async () => {
    mocks.account.mockRejectedValueOnce(new Error('unauthorized'))
    await render('/cards/hearts'); expect(mocks.fetch).not.toHaveBeenCalled()
    let finish!: (value: { userId: string }) => void
    mocks.account.mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    await render('/games/mines'); await render('/home')
    await act(async () => finish({ userId: 'alice' })); expect(mocks.fetch).not.toHaveBeenCalled()
  })
  it('sends initial and 25-second heartbeats only while visible and focused, then closes its lease', async () => {
    await render('/cards/hearts')
    expect(signals().map(signal => signal.visible)).toEqual([true])
    expect(mocks.fetch).toHaveBeenCalledWith('/api/games/activity/presence', expect.objectContaining({
      method: 'POST', keepalive: true, headers: { 'Content-Type': 'application/json', 'X-FortuneForge-Presence': '1' },
    }))
    await act(async () => vi.advanceTimersByTimeAsync(25_000))
    visible = false; await act(async () => document.dispatchEvent(new Event('visibilitychange')))
    await act(async () => vi.advanceTimersByTimeAsync(100_000))
    expect(signals().map(signal => signal.visible)).toEqual([true, true, false])
    visible = true; focused = false
    await act(async () => document.dispatchEvent(new Event('visibilitychange')))
    expect(signals()).toHaveLength(3)
    focused = true; await act(async () => window.dispatchEvent(new Event('focus')))
    await render('/home')
    expect(signals().map(signal => signal.visible)).toEqual([true, true, false, true, false])
    expect(new Set(signals().map(signal => signal.sessionId)).size).toBe(1)
  })
  it('serializes hidden after a pending visible request and stops after lost authentication', async () => {
    let finish!: (response: Response) => void
    mocks.fetch.mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    await render('/games/mines'); await render('/home')
    expect(signals()).toHaveLength(1)
    await act(async () => finish(new Response(null, { status: 204 })))
    expect(signals().map(signal => signal.visible)).toEqual([true, false])
    mocks.fetch.mockResolvedValue(new Response(null, { status: 401 }))
    await render('/games/craps'); await act(async () => vi.advanceTimersByTimeAsync(100_000))
    expect(signals()).toHaveLength(3)
  })
})

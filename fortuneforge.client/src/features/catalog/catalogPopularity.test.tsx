// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { orderByCataloguePopularity, parseCataloguePopularity, useCataloguePopularity } from './catalogPopularity'

const game = (id: string, playCount: number | null) => ({ id, route: `/games/${id}`, playCount })
const payload = { metric: 'recorded-plays', trackingStartedAtUtc: '2026-10-26T12:00:00Z', games: [game('craps', 4)] }
const response = (data: unknown) => ({ ok: true, json: async () => data }) as Response

let root: Root | null = null
beforeEach(() => vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', 'true'))
afterEach(async () => { if (root) await act(async () => root?.unmount()); root = null; vi.unstubAllGlobals(); vi.unstubAllEnvs(); vi.useRealTimers() })

async function mountHook() {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  let current: ReturnType<typeof useCataloguePopularity> | undefined
  function Harness() { current = useCataloguePopularity(); return null }
  root = createRoot(document.createElement('div'))
  await act(async () => { root!.render(<Harness />) })
  return { get current() { return current! } }
}

describe('global catalogue popularity', () => {
  it('keeps popularity unknown without requests, polling or retry notices when disabled', async () => {
    vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', undefined)
    vi.useFakeTimers()
    const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
    const result = await mountHook()
    await act(async () => { window.dispatchEvent(new Event('focus')); result.current.reload(); await vi.advanceTimersByTimeAsync(120_000) })
    expect(fetchMock).not.toHaveBeenCalled()
    expect(result.current).toMatchObject({ games: null, trackingStartedAtUtc: null, isLoading: false, error: '' })
  })
  it('orders actual counts across categories with deterministic ties and unknowns last without mutating the catalogue', () => {
    const games = [
      { href: '/cards/hearts', title: 'Hearts' },
      { href: '/games/craps', title: 'Craps' },
      { href: '/slots/wukong', title: 'Wukong' },
      { href: '/games/mines', title: 'Mines' },
    ]
    const before = [...games]
    const counts = [game('craps', 4), { id: 'wukong-journey-to-the-west', route: '/slots/wukong', playCount: 7 },
      { id: 'hearts', route: '/cards/hearts', playCount: 4 }]
    expect(orderByCataloguePopularity(games, counts).map(item => item.title)).toEqual(['Wukong', 'Craps', 'Hearts', 'Mines'])
    expect(games).toEqual(before)
    expect(orderByCataloguePopularity(games, null).map(item => item.title)).toEqual(['Craps', 'Hearts', 'Mines', 'Wukong'])
  })

  it('accepts unknown counts before tracking starts, without claiming zero plays', () => {
    const result = parseCataloguePopularity({ ...payload, trackingStartedAtUtc: null, games: [game('craps', null)] })
    expect(result.games[0].playCount).toBeNull()
  })

  it.each([
    { ...payload, games: [game('craps', -1)] },
    { ...payload, games: [game('craps', 1.2)] },
    { ...payload, games: [game('craps', Number.MAX_SAFE_INTEGER + 1)] },
    { ...payload, games: [game('craps', 1), game('craps', 2)] },
    { ...payload, trackingStartedAtUtc: null },
    { ...payload, trackingStartedAtUtc: 'invalid' },
  ])('rejects untrustworthy payload %#', invalid => {
    expect(() => parseCataloguePopularity(invalid)).toThrow('temporarily unavailable')
  })

  it('loads server counts and retains the last genuine snapshot during a failed refresh', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(response(payload)).mockRejectedValueOnce(new Error('offline'))
    vi.stubGlobal('fetch', fetchMock)
    const result = await mountHook()
    expect(result.current.isLoading).toBe(false)
    expect(result.current.games).toEqual(payload.games)
    expect(result.current.trackingStartedAtUtc).toBe(payload.trackingStartedAtUtc)
    await act(async () => result.current.reload())
    expect(result.current.error).toContain('temporarily unavailable')
    expect(result.current.games).toEqual(payload.games)
    expect(fetchMock.mock.calls.every(([url]) => url === '/api/catalog/popularity')).toBe(true)
  })

  it('returns no invented popularity on initial failure and aborts pending work on unmount', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false } as Response)
    vi.stubGlobal('fetch', fetchMock)
    const result = await mountHook()
    expect(result.current.error).not.toBe('')
    expect(result.current.games).toBeNull()
    const signal = fetchMock.mock.calls[0][1].signal as AbortSignal
    await act(async () => root!.unmount())
    root = null
    expect(signal.aborted).toBe(true)
  })
})

// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({ fetch: vi.fn<typeof fetch>() }))
vi.mock('../account/services/accountsApi', () => ({ fetchWithAccountSession: api.fetch }))
let store: typeof import('./gameExperienceStore')
let events: typeof import('../account/services/accountSessionEvents')
let ids: readonly string[]
const cleanups: (() => void)[] = []
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
function body(userId = 'player-a', positive?: string) {
  return { schemaVersion: 1, userId, generatedAtUtc: '2026-11-02T00:00:00Z', games: ids.map(gameId => ({
    gameId, state: gameId === positive ? 'known' : 'unknown', hasPlayed: gameId === positive ? true : null,
    provenance: gameId === positive ? ['accepted-activity'] : [], coverage: 'partial', firstPlayedAtUtc: null,
  })) }
}
function negativeBody(positive?: string) {
  const value = body('player-a', positive)
  Object.assign(value.games.find(row => row.gameId === 'keno')!, {
    state: 'known', hasPlayed: false, provenance: ['complete-authoritative-coverage'], coverage: 'complete',
  })
  return value
}
function mutationSignals() {
  const channels: EventTarget[] = []
  vi.stubGlobal('BroadcastChannel', class extends EventTarget {
    constructor() { super(); channels.push(this) }
    postMessage() {}
    close() {}
  })
  return (source: 'local' | 'storage' | 'broadcast') => {
    if (source === 'local') events.publishAccountSessionEvent('accepted-mutation')
    else if (source === 'storage') window.dispatchEvent(new StorageEvent('storage', {
      key: 'fortune-forge.game-experience-invalidation', newValue: 'refresh:accepted-play',
    }))
    else channels[0]!.dispatchEvent(new MessageEvent('message', { data: 'refresh' }))
  }
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }
async function flush() { for (let n = 0; n < 15; n++) await Promise.resolve() }
beforeEach(async () => {
  vi.resetModules(); vi.useFakeTimers(); api.fetch.mockReset(); localStorage.clear()
  store = await import('./gameExperienceStore')
  events = await import('../account/services/accountSessionEvents')
  ids = (await import('./gameExperienceIdentity')).CANONICAL_EXPERIENCE_GAME_IDS
  api.fetch.mockImplementation(async () => response(body()))
})
afterEach(() => { cleanups.splice(0).forEach(cleanup => cleanup()); vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('authoritative shared player-game read', () => {
  it('requires every canonical title and keeps unknown distinct from a proven negative', () => {
    expect(ids).toHaveLength(45)
    const value = body(), row = value.games[0]!
    expect(store.parseExperienceResponse(value).games.every(item => item.hasPlayed === null)).toBe(true)
    Object.assign(row, { state: 'known', hasPlayed: false, provenance: ['complete-authoritative-coverage'], coverage: 'complete' })
    expect(store.parseExperienceResponse(value).games[0]!.hasPlayed).toBe(false)
    row.coverage = 'partial'
    expect(() => store.parseExperienceResponse(value)).toThrow(/unproven/)
    expect(() => store.parseExperienceResponse({ ...body(), games: body().games.slice(1) })).toThrow(/omitted/)
    const duplicate = body(); duplicate.games[1] = duplicate.games[0]!
    expect(() => store.parseExperienceResponse(duplicate)).toThrow(/invalid/)
  })

  it('deduplicates concurrent loaders and caches one small batch across titles', async () => {
    const waiting = deferred<Response>(); api.fetch.mockReturnValueOnce(waiting.promise)
    const first = store.loadGameExperiences(), second = store.loadGameExperiences()
    expect(first).toBe(second); expect(api.fetch).toHaveBeenCalledTimes(1)
    expect(store.getExperienceSnapshot()).toMatchObject({ status: 'loading', data: null })
    waiting.resolve(response(body('player-a', 'blackjack'))); await first
    await store.loadGameExperiences()
    expect(api.fetch).toHaveBeenCalledTimes(1)
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'blackjack')?.hasPlayed).toBe(true)
    expect(api.fetch).toHaveBeenCalledWith('/api/games/experience', expect.objectContaining({ method: 'GET', cache: 'no-store' }))
  })

  it('preserves positive evidence for the same player and never carries it to another player', async () => {
    api.fetch.mockResolvedValueOnce(response(body('player-a', 'blackjack')))
    await store.loadGameExperiences()
    await store.loadGameExperiences(true)
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'blackjack')?.hasPlayed).toBe(true)
    api.fetch.mockResolvedValueOnce(response(body('player-b')))
    await store.loadGameExperiences(true)
    expect(store.getExperienceSnapshot().data?.userId).toBe('player-b')
    expect(store.getExperienceSnapshot().data?.games.every(row => row.hasPlayed === null)).toBe(true)
  })

  it('does not expose an old account during inactive-cache reuse or delayed responses', async () => {
    api.fetch.mockResolvedValueOnce(response(body('player-a', 'blackjack')))
    await store.loadGameExperiences()
    events.publishAccountSessionEvent('session-changed')
    expect(store.getExperienceSnapshot()).toMatchObject({ status: 'loading', data: null })
    const waiting = deferred<Response>(); api.fetch.mockReturnValueOnce(waiting.promise)
    const old = store.loadGameExperiences()
    cleanups.push(store.subscribeGameExperiences(() => {}))
    api.fetch.mockResolvedValueOnce(response(body('player-b')))
    events.publishAccountSessionEvent('session-changed'); await flush()
    waiting.resolve(response(body('player-a', 'blackjack'))); await old; await flush()
    expect(store.getExperienceSnapshot().data?.userId).toBe('player-b')
    expect(store.getExperienceSnapshot().data?.games.every(row => row.hasPlayed === null)).toBe(true)
  })

  it.each([500, 404, 403])('keeps API %s as an explicit error without false knowledge', async status => {
    api.fetch.mockResolvedValueOnce(response({}, status)); await store.loadGameExperiences()
    expect(store.getExperienceSnapshot()).toMatchObject({ status: 'error', data: null })
  })

  it('represents an unauthenticated session explicitly', async () => {
    api.fetch.mockResolvedValueOnce(response({}, 401)); await store.loadGameExperiences()
    expect(store.getExperienceSnapshot()).toEqual({ status: 'guest', data: null, error: null })
  })

  it('bounds a stalled read and permits an explicit retry without inventing a flag', async () => {
    api.fetch.mockImplementationOnce((_path, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('Timed out', 'AbortError')), { once: true })
    }))
    const pending = store.loadGameExperiences()
    await vi.advanceTimersByTimeAsync(5_000); await pending
    expect(store.getExperienceSnapshot()).toMatchObject({ status: 'error', data: null })
    await store.loadGameExperiences(true)
    expect(store.getExperienceSnapshot().status).toBe('ready')
    expect(store.getExperienceSnapshot().data?.games.every(row => row.hasPlayed === null)).toBe(true)
  })

  it('debounces successful-request invalidation and trusts only the next server read', async () => {
    cleanups.push(store.subscribeGameExperiences(() => {})); await store.loadGameExperiences()
    for (let n = 0; n < 20; n++) events.publishAccountSessionEvent('accepted-mutation')
    expect(api.fetch).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(150); await flush()
    expect(api.fetch).toHaveBeenCalledTimes(2)
    expect(store.getExperienceSnapshot().data?.games.every(row => row.hasPlayed === null)).toBe(true)
    window.dispatchEvent(new StorageEvent('storage', { key: 'fortune-forge.game-experience-invalidation', newValue: '{"hasPlayed":true}' }))
    await vi.advanceTimersByTimeAsync(150); await flush()
    expect(api.fetch).toHaveBeenCalledTimes(3)
    expect(store.getExperienceSnapshot().data?.games.every(row => row.hasPlayed === null)).toBe(true)
  })

  it.each(['local', 'storage', 'broadcast'] as const)('invalidates a cached negative immediately on %s play signals while retaining proven positives', async source => {
    const signal = mutationSignals()
    cleanups.push(store.subscribeGameExperiences(() => {}))
    api.fetch.mockResolvedValueOnce(response(negativeBody('blackjack')))
    await store.loadGameExperiences()
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'keno')?.hasPlayed).toBe(false)
    const fresh = deferred<Response>(); api.fetch.mockReturnValueOnce(fresh.promise)
    signal(source)
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'keno')).toMatchObject({
      state: 'unknown', hasPlayed: null, coverage: 'partial', provenance: [],
    })
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'blackjack')?.hasPlayed).toBe(true)
    expect(api.fetch).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(150); await flush()
    expect(api.fetch).toHaveBeenCalledTimes(2)
    // A fresh authoritative read may still prove that a different title is unplayed.
    fresh.resolve(response(negativeBody())); await flush()
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'keno')?.hasPlayed).toBe(false)
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'blackjack')?.hasPlayed).toBe(true)
  })

  it.each(['local', 'storage', 'broadcast'] as const)('filters a pre-%s-mutation response and refreshes after an in-flight batch even when its debounce already fired', async source => {
    const signal = mutationSignals(), observed: ReturnType<typeof store.getExperienceSnapshot>[] = []
    cleanups.push(store.subscribeGameExperiences(() => observed.push(store.getExperienceSnapshot())))
    api.fetch.mockResolvedValueOnce(response(negativeBody('blackjack')))
    await store.loadGameExperiences()
    const late = deferred<Response>(), fresh = deferred<Response>()
    api.fetch.mockReturnValueOnce(late.promise).mockReturnValueOnce(fresh.promise)
    const old = store.loadGameExperiences(true), start = observed.length
    signal(source)
    await vi.advanceTimersByTimeAsync(150); await flush()
    expect(api.fetch).toHaveBeenCalledTimes(2)
    late.resolve(response(negativeBody()))
    await old; await flush()
    expect(api.fetch).toHaveBeenCalledTimes(3)
    const afterMutation = observed.slice(start)
    expect(afterMutation.some(value => value.data?.games.some(row => row.hasPlayed === false))).toBe(false)
    expect(afterMutation.some(value => value.data?.games.find(row => row.gameId === 'keno')?.state === 'unknown')).toBe(true)
    fresh.resolve(response(body('player-a', 'keno'))); await flush()
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'keno')?.hasPlayed).toBe(true)
    expect(store.getExperienceSnapshot().data?.games.find(row => row.gameId === 'blackjack')?.hasPlayed).toBe(true)
    api.fetch.mockResolvedValueOnce(response(body('player-b')))
    events.publishAccountSessionEvent('session-changed'); await flush()
    expect(store.getExperienceSnapshot().data?.userId).toBe('player-b')
    expect(store.getExperienceSnapshot().data?.games.every(row => row.hasPlayed === null)).toBe(true)
  })

  it('revalidates on return and removes observers after the last consumer', async () => {
    const stop = store.subscribeGameExperiences(() => {}); await store.loadGameExperiences()
    window.dispatchEvent(new Event('focus')); await flush()
    expect(api.fetch).toHaveBeenCalledTimes(2)
    stop()
    events.publishAccountSessionEvent('accepted-mutation'); window.dispatchEvent(new Event('focus'))
    await vi.advanceTimersByTimeAsync(31_000); await flush()
    expect(api.fetch).toHaveBeenCalledTimes(2)
  })

  it('never reuses an account cache after an unobserved cookie-only account change', async () => {
    const stop = store.subscribeGameExperiences(() => {})
    api.fetch.mockResolvedValueOnce(response(body('player-a', 'blackjack')))
    await store.loadGameExperiences(); stop()
    // No listener exists to observe this other-tab cookie/session transition.
    window.dispatchEvent(new StorageEvent('storage', { key: 'fortune-forge.game-experience-invalidation', newValue: 'session-changed:1' }))
    expect(store.getExperienceSnapshot()).toMatchObject({ status: 'loading', data: null })
    api.fetch.mockResolvedValueOnce(response(body('player-b')))
    cleanups.push(store.subscribeGameExperiences(() => {}))
    await store.loadGameExperiences()
    expect(api.fetch).toHaveBeenCalledTimes(2)
    expect(store.getExperienceSnapshot().data?.userId).toBe('player-b')
    expect(store.getExperienceSnapshot().data?.games.every(row => row.hasPlayed === null)).toBe(true)
  })

  it('clears immediately on a cross-tab session signal without trusting its data', async () => {
    cleanups.push(store.subscribeGameExperiences(() => {}))
    api.fetch.mockResolvedValueOnce(response(body('player-a', 'blackjack')))
    await store.loadGameExperiences()
    const waiting = deferred<Response>(); api.fetch.mockReturnValueOnce(waiting.promise)
    window.dispatchEvent(new StorageEvent('storage', { key: 'fortune-forge.game-experience-invalidation', newValue: 'session-changed:2' }))
    expect(store.getExperienceSnapshot()).toMatchObject({ status: 'loading', data: null })
    waiting.resolve(response(body('player-b'))); await flush()
    expect(store.getExperienceSnapshot().data?.userId).toBe('player-b')
  })

  it('keeps the playable route working when optional BroadcastChannel construction is blocked', async () => {
    vi.stubGlobal('BroadcastChannel', class { constructor() { throw new Error('Blocked by privacy policy') } })
    let stop!: () => void
    expect(() => { stop = store.subscribeGameExperiences(() => {}) }).not.toThrow()
    cleanups.push(stop)
    await store.loadGameExperiences()
    expect(store.getExperienceSnapshot().status).toBe('ready')
  })
})

// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
const mocks = vi.hoisted(() => ({ fetch: vi.fn(), slots: vi.fn(), cards: vi.fn() }))
vi.mock('../account/services/accountsApi', () => ({ fetchWithAccountSession: mocks.fetch, getSlotHistory: mocks.slots }))
vi.mock('../../games/cards/shared/cardRoomHistoryApi', () => ({ getCardRoomHistory: mocks.cards }))
import { getRecentGameActivity, mergeRecentGameActivity, useRecentGameActivity } from './recentGameActivity'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'

let root: Root | undefined
let latest: ReturnType<typeof useRecentGameActivity>
const play = (gameId: string, hour = 12) => ({ gameId, playedAtUtc: `2026-10-26T${hour}:00:00Z` })
function Probe({ userId }: { userId?: string }) { latest = useRecentGameActivity(userId); return <span>{latest.games.map(game => game.gameId).join(',')}</span> }
const response = (games: unknown[]) => new Response(JSON.stringify({ games }), { status: 200 })
beforeEach(() => {
  vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', 'true')
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  mocks.fetch.mockReset().mockResolvedValue(response([])); mocks.slots.mockReset().mockResolvedValue({ spins: [] }); mocks.cards.mockReset().mockResolvedValue([])
})
afterEach(async () => { if (root) await act(() => root!.unmount()); root = undefined; vi.unstubAllGlobals(); vi.unstubAllEnvs() })
async function mount(userId = 'alice') {
  const host = document.createElement('div'); root = createRoot(host)
  await act(async () => { root!.render(<Probe userId={userId} />) }); return host
}

describe('recent game activity', () => {
  it('uses completed legacy history with no new API requests or unavailable notice by default', async () => {
    vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', undefined)
    mocks.cards.mockResolvedValue([{ game: 'texas-holdem', completedAtUtc: '2026-10-26T14:00:00Z' }])
    mocks.slots.mockResolvedValue({ spins: [{ gameId: 'rainbow-realm-fruits-v1', createdAtUtc: '2026-10-26T15:00:00Z' }] })
    await mount()
    expect(latest.games.map(game => game.gameId)).toEqual(['rainbow-realm', 'texas-holdem'])
    expect(latest.error).toBeNull(); expect(latest.isLoading).toBe(false)
    expect(mocks.fetch).not.toHaveBeenCalled()
    expect(await getRecentGameActivity()).toEqual([play('rainbow-realm', 15), play('texas-holdem', 14)])
    expect(mocks.fetch).not.toHaveBeenCalled()
  })
  it('refreshes legacy recents on focus and isolates the next account while the new API is disabled', async () => {
    vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', 'false')
    mocks.cards.mockResolvedValueOnce([{ game: 'blackjack', completedAtUtc: '2026-10-26T14:00:00Z' }])
    await mount()
    mocks.cards.mockResolvedValueOnce([{ game: 'texas-holdem', completedAtUtc: '2026-10-26T16:00:00Z' }])
    await act(async () => window.dispatchEvent(new Event('focus')))
    expect(latest.games[0]).toEqual(play('texas-holdem', 16))
    expect(mocks.cards).toHaveBeenCalledTimes(2); expect(mocks.slots).toHaveBeenCalledTimes(2)
    mocks.cards.mockResolvedValue([{ game: 'solitaire', completedAtUtc: '2026-10-26T17:00:00Z' }])
    await act(async () => root!.render(<Probe userId="bob" />))
    expect(latest.games).toEqual([play('solitaire', 17)])
    expect(mocks.fetch).not.toHaveBeenCalled(); expect(latest.error).toBeNull()
  })
  it('keeps genuine recents through legacy history failures and still reports actual failures', async () => {
    vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', 'false')
    mocks.cards.mockResolvedValueOnce([{ game: 'blackjack', completedAtUtc: '2026-10-26T14:00:00Z' }])
    await mount()
    mocks.cards.mockRejectedValueOnce(new Error('offline')); mocks.slots.mockRejectedValueOnce(new Error('offline'))
    await act(async () => latest.refresh())
    expect(latest.games).toEqual([play('blackjack', 14)])
    expect(latest.error).toContain('temporarily unavailable')
    expect(mocks.fetch).not.toHaveBeenCalled()
  })
  it('supports all forty canonical games and sorts real timestamps without making up activity', () => {
    const ids = ['blackjack', 'dragon-tiger', 'mines', 'texas-holdem', 'solitaire', 'three-card-poker', 'house-holdem', 'hearts', 'andar-bahar', 'hi-lo', 'baccarat', 'casino-war', 'video-poker', 'roulette', 'craps', 'keno', 'sic-bo', 'liars-dice', '2048', 'drop-merge', ...SLOT_ROUTE_DEFINITIONS.map(game => game.id)]
    const result = mergeRecentGameActivity(ids.map(id => play(id)), [play('blackjack', 15), play('horse-flight', 16), { gameId: 'mines', playedAtUtc: 'invalid' }])
    expect(result).toHaveLength(40); expect(result[0]).toEqual(play('blackjack', 15))
    expect(mergeRecentGameActivity()).toEqual([])
  })
  it('uses the authenticated endpoint and rejects malformed responses', async () => {
    mocks.fetch.mockResolvedValueOnce(response([play('hearts'), { gameId: 'keno' }, play('invented')]))
    expect(await getRecentGameActivity()).toEqual([play('hearts')])
    expect(mocks.fetch).toHaveBeenCalledWith('/api/catalog/recent-games', expect.objectContaining({ method: 'GET', cache: 'no-store' }))
    mocks.fetch.mockResolvedValueOnce(new Response('{}'))
    await expect(getRecentGameActivity()).rejects.toThrow('invalid response')
  })
  it('merges completed historical results without letting table creation or queues outrank actual play', async () => {
    mocks.fetch.mockResolvedValue(response([play('keno', 16), play('texas-holdem', 17)]))
    mocks.cards.mockResolvedValue([
      { game: 'blackjack', completedAtUtc: null, startedAtUtc: '2026-10-26T23:00:00Z' },
      { game: 'texas-holdem', completedAtUtc: '2026-10-26T14:00:00Z' },
    ])
    mocks.slots.mockResolvedValue({ spins: [{ gameId: 'rainbow-realm-fruits-v1', createdAtUtc: '2026-10-26T15:00:00Z' }] })
    await mount()
    expect(latest.games.map(game => game.gameId)).toEqual(['texas-holdem', 'keno', 'rainbow-realm'])
  })
  it('refreshes on return to the tab and prevents cross-account or signed-out results', async () => {
    mocks.fetch.mockImplementation(async () => response([play('mines')]))
    await mount(); expect(latest.games).toEqual([play('mines')])
    mocks.fetch.mockImplementation(async () => response([play('sic-bo', 15)]))
    await act(async () => window.dispatchEvent(new Event('focus')))
    expect(latest.games).toEqual([play('sic-bo', 15)])
    expect(mocks.cards).toHaveBeenCalledTimes(1); expect(mocks.slots).toHaveBeenCalledTimes(1)
    await act(async () => { root!.render(<Probe />) })
    expect(latest.games).toEqual([]); expect(latest.isLoading).toBe(false)
    await act(async () => { root!.render(<Probe userId="bob" />) })
    expect(latest.games).toEqual([play('sic-bo', 15)])
    expect(mocks.cards).toHaveBeenCalledTimes(2); expect(mocks.slots).toHaveBeenCalledTimes(2)
  })
  it('ignores an old account response that finishes after switching accounts', async () => {
    let finish!: (value: Response) => void
    mocks.fetch.mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
    await mount()
    mocks.fetch.mockImplementation(async () => response([play('hearts')]))
    await act(async () => root!.render(<Probe userId="bob" />))
    await act(async () => finish(response([play('blackjack', 23)])))
    expect(latest.games).toEqual([play('hearts')])
  })
  it('keeps the last confirmed same-account activity if a refresh fails, then recovers', async () => {
    mocks.fetch.mockImplementation(async () => response([play('mines', 16)]))
    mocks.cards.mockResolvedValue([{ game: 'texas-holdem', completedAtUtc: '2026-10-26T14:00:00Z' }])
    await mount()
    mocks.fetch.mockRejectedValueOnce(new Error('temporarily offline'))
    await act(async () => window.dispatchEvent(new Event('focus')))
    expect(latest.games.map(game => game.gameId)).toEqual(['mines', 'texas-holdem'])
    expect(latest.error).toContain('temporarily unavailable')
    mocks.fetch.mockImplementation(async () => response([play('keno', 17), play('mines', 16)]))
    await act(async () => latest.refresh())
    expect(latest.games[0]).toEqual(play('keno', 17)); expect(latest.error).toBeNull()
  })
})

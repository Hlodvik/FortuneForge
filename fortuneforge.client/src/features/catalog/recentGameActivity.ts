import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchWithAccountSession, getSlotHistory } from '../account/services/accountsApi'
import { getCardRoomHistory } from '../../games/cards/shared/cardRoomHistoryApi'
import { findSlotRouteByServerId, SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { isGameActivityApiEnabled } from './gameActivityCapability'

export type RecentGameActivity = Readonly<{ gameId: string; playedAtUtc: string }>
const gameIds = new Set([
  'blackjack', 'dragon-tiger', 'mines', 'texas-holdem', 'solitaire', 'three-card-poker',
  'house-holdem', 'hearts', 'andar-bahar', 'hi-lo', 'baccarat', 'casino-war', 'video-poker',
  'roulette', 'craps', 'keno', 'sic-bo', 'liars-dice', '2048', 'drop-merge',
  ...SLOT_ROUTE_DEFINITIONS.map((game) => game.id),
])

export function mergeRecentGameActivity(...sources: readonly (readonly RecentGameActivity[])[]): RecentGameActivity[] {
  const latest = new Map<string, RecentGameActivity>()
  for (const source of sources) for (const item of source) {
    if (!gameIds.has(item.gameId) || !Number.isFinite(Date.parse(item.playedAtUtc))) continue
    const prior = latest.get(item.gameId)
    if (prior === undefined || Date.parse(item.playedAtUtc) > Date.parse(prior.playedAtUtc)) latest.set(item.gameId, item)
  }
  return [...latest.values()].sort((a, b) => Date.parse(b.playedAtUtc) - Date.parse(a.playedAtUtc) || a.gameId.localeCompare(b.gameId))
}

export async function getRecentGameActivity(signal?: AbortSignal): Promise<RecentGameActivity[]> {
  if (!isGameActivityApiEnabled()) return getHistoricalActivity(signal)
  const response = await fetchWithAccountSession('/api/catalog/recent-games', { method: 'GET', cache: 'no-store', signal })
  if (!response.ok) throw new Error('Recent games could not be loaded.')
  const body: unknown = await response.json()
  if (typeof body !== 'object' || body === null || !('games' in body) || !Array.isArray(body.games)) {
    throw new Error('Recent games returned an invalid response.')
  }
  return mergeRecentGameActivity(body.games.filter((item): item is RecentGameActivity =>
    typeof item === 'object' && item !== null && typeof item.gameId === 'string' && typeof item.playedAtUtc === 'string'))
}

// Retain genuine historical completed plays while the all-game activity index
// fills. Queue creation and table-start times are deliberately not used.
async function getHistoricalActivity(signal?: AbortSignal): Promise<RecentGameActivity[]> {
  const [cards, slots] = await Promise.allSettled([getCardRoomHistory(100, signal), getSlotHistory(100)])
  if (cards.status === 'rejected' && slots.status === 'rejected') throw new Error('Recent activity is temporarily unavailable.')
  return mergeRecentGameActivity(
    cards.status === 'fulfilled' ? cards.value.flatMap((item) => item.completedAtUtc === null ? [] : [{ gameId: item.game, playedAtUtc: item.completedAtUtc }]) : [],
    slots.status === 'fulfilled' ? slots.value.spins.flatMap((item) => {
      const game = findSlotRouteByServerId(item.gameId)
      return game === null ? [] : [{ gameId: game.id, playedAtUtc: item.createdAtUtc }]
    }) : [],
  )
}

type RecentState = { userId: string | undefined; games: RecentGameActivity[]; isLoading: boolean; error: string | null }
export function useRecentGameActivity(userId: string | undefined) {
  const activityApiEnabled = isGameActivityApiEnabled()
  const [revision, setRevision] = useState(0)
  const history = useRef<{ userId: string; promise: Promise<RecentGameActivity[]> } | null>(null)
  const [state, setState] = useState<RecentState>({ userId, games: [], isLoading: userId !== undefined, error: null })
  const refresh = useCallback(() => setRevision((value) => value + 1), [])

  useEffect(() => {
    if (userId === undefined) {
      history.current = null
      setState({ userId, games: [], isLoading: false, error: null })
      return undefined
    }
    const controller = new AbortController()
    if (!activityApiEnabled) {
      history.current = null
      setState((prior) => ({ userId, games: prior.userId === userId ? prior.games : [], isLoading: true, error: null }))
      // Hosting-only releases use the existing completed-result APIs. Refresh
      // those on return to Home until the bounded activity index is deployed.
      void getHistoricalActivity(controller.signal).then(games => {
        if (controller.signal.aborted) return
        setState(prior => ({ userId, games: mergeRecentGameActivity(prior.userId === userId ? prior.games : [], games), isLoading: false, error: null }))
      }).catch(() => {
        if (controller.signal.aborted) return
        setState(prior => ({ userId, games: prior.userId === userId ? prior.games : [], isLoading: false, error: 'Recent activity is temporarily unavailable.' }))
      })
      return () => controller.abort()
    }
    if (history.current?.userId !== userId) history.current = { userId, promise: getHistoricalActivity() }
    setState((prior) => ({ userId, games: prior.userId === userId ? prior.games : [], isLoading: true, error: null }))
    // Legacy result scans run once per account/mount. Returning focus refreshes
    // only the bounded all-game activity index, not the growing old ledgers.
    void Promise.allSettled([getRecentGameActivity(controller.signal), history.current.promise])
      .then(([current, historical]) => {
        if (controller.signal.aborted) return
        setState((prior) => ({ userId, isLoading: false,
          games: mergeRecentGameActivity(current.status === 'fulfilled' ? current.value : prior.userId === userId ? prior.games : [], historical.status === 'fulfilled' ? historical.value : []),
          error: current.status === 'rejected' ? 'Recent activity is temporarily unavailable.' : null,
        }))
      })
    return () => controller.abort()
  }, [userId, revision, activityApiEnabled])

  useEffect(() => {
    if (userId === undefined) return undefined
    const onVisible = () => { if (document.visibilityState === 'visible') refresh() }
    window.addEventListener('focus', onVisible)
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      window.removeEventListener('focus', onVisible)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [userId, refresh])

  // Do not expose the previous account's results even for one render between
  // an auth change and effect cleanup.
  return state.userId === userId
    ? { games: state.games, isLoading: state.isLoading, error: state.error, refresh }
    : { games: [], isLoading: userId !== undefined, error: null, refresh }
}

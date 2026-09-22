import { useEffect, useState } from 'react'
import { getSlotHistory } from '../../features/account/services/accountsApi'
import { loadSlotGameCatalogById } from './catalogLoaders'
import type { SlotGameCatalogEntry } from './catalogTypes'
import { findSlotRouteByServerId } from './routeRegistry'

type PlayedSlotGame = {
  game: SlotGameCatalogEntry
  playedAtUtc: string
}

type RecentSlotGameState = {
  error: string | null
  game: SlotGameCatalogEntry | null
  games: readonly PlayedSlotGame[]
  isLoading: boolean
  playedAtUtc: string | null
}

// Recent play comes from authenticated spin history, keeping it scoped to the
// active account and consistent across browsers and devices.
export function useRecentSlotGame(userId: string | undefined): RecentSlotGameState {
  const [state, setState] = useState<RecentSlotGameState>({
    error: null,
    game: null,
    games: [],
    isLoading: userId !== undefined,
    playedAtUtc: null,
  })

  useEffect(() => {
    if (userId === undefined) {
      setState({ error: null, game: null, games: [], isLoading: false, playedAtUtc: null })
      return undefined
    }

    let isActive = true
    setState({ error: null, game: null, games: [], isLoading: true, playedAtUtc: null })
    void getSlotHistory(100)
      .then(async ({ spins }) => {
        const seenGameIds = new Set<string>()
        const playedRoutes = spins.flatMap((spin) => {
          const route = findSlotRouteByServerId(spin.gameId)
          if (route === null || seenGameIds.has(route.id)) return []
          seenGameIds.add(route.id)
          return [{ playedAtUtc: spin.createdAtUtc, routeId: route.id }]
        })
        const loadedGames = await Promise.all(playedRoutes.map(async ({ playedAtUtc, routeId }) => ({
          game: await loadSlotGameCatalogById(routeId),
          playedAtUtc,
        })))
        const games = loadedGames.flatMap(({ game, playedAtUtc }) =>
          game === null ? [] : [{ game, playedAtUtc }],
        )
        if (isActive) {
          setState({
            error: null,
            game: games[0]?.game ?? null,
            games,
            isLoading: false,
            playedAtUtc: games[0]?.playedAtUtc ?? null,
          })
        }
      })
      .catch((error: unknown) => {
        if (isActive) {
          setState({
            error: error instanceof Error ? error.message : 'Recent play could not be loaded.',
            game: null,
            games: [],
            isLoading: false,
            playedAtUtc: null,
          })
        }
      })

    return () => {
      isActive = false
    }
  }, [userId])

  return state
}

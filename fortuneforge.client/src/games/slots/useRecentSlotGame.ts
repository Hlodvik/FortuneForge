import { useEffect, useState } from 'react'
import { getSlotHistory } from '../../features/account/services/accountsApi'
import { loadSlotGameCatalogById } from './catalogLoaders'
import type { SlotGameCatalogEntry } from './catalogTypes'
import { findSlotRouteByServerId } from './routeRegistry'

type RecentSlotGameState = {
  error: string | null
  game: SlotGameCatalogEntry | null
  isLoading: boolean
  playedAtUtc: string | null
}

// Recent play comes from authenticated spin history, keeping it scoped to the
// active account and consistent across browsers and devices.
export function useRecentSlotGame(userId: string | undefined): RecentSlotGameState {
  const [state, setState] = useState<RecentSlotGameState>({
    error: null,
    game: null,
    isLoading: userId !== undefined,
    playedAtUtc: null,
  })

  useEffect(() => {
    if (userId === undefined) {
      setState({ error: null, game: null, isLoading: false, playedAtUtc: null })
      return undefined
    }

    let isActive = true
    setState({ error: null, game: null, isLoading: true, playedAtUtc: null })
    void getSlotHistory(1)
      .then(async ({ spins }) => {
        const route = spins[0] === undefined ? null : findSlotRouteByServerId(spins[0].gameId)
        const game = route === null ? null : await loadSlotGameCatalogById(route.id)
        if (isActive) {
          setState({
            error: null,
            game,
            isLoading: false,
            playedAtUtc: spins[0]?.createdAtUtc ?? null,
          })
        }
      })
      .catch((error: unknown) => {
        if (isActive) {
          setState({
            error: error instanceof Error ? error.message : 'Recent play could not be loaded.',
            game: null,
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

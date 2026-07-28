import { useEffect, useState } from 'react'
import { getSlotHistory } from '../landing/services/accountsApi'
import { findSlotGameByServerId, type SlotGameCatalogEntry } from './gameCatalog'

type RecentSlotGameState = {
  error: string | null
  game: SlotGameCatalogEntry | null
  isLoading: boolean
}

// Recent play comes from authenticated spin history, keeping it scoped to the
// active account and consistent across browsers and devices.
export function useRecentSlotGame(userId: string | undefined): RecentSlotGameState {
  const [state, setState] = useState<RecentSlotGameState>({
    error: null,
    game: null,
    isLoading: userId !== undefined,
  })

  useEffect(() => {
    if (userId === undefined) {
      setState({ error: null, game: null, isLoading: false })
      return undefined
    }

    let isActive = true
    setState({ error: null, game: null, isLoading: true })
    void getSlotHistory(1)
      .then(({ spins }) => {
        if (isActive) {
          setState({
            error: null,
            game: spins[0] === undefined ? null : findSlotGameByServerId(spins[0].gameId),
            isLoading: false,
          })
        }
      })
      .catch((error: unknown) => {
        if (isActive) {
          setState({
            error: error instanceof Error ? error.message : 'Recent play could not be loaded.',
            game: null,
            isLoading: false,
          })
        }
      })

    return () => {
      isActive = false
    }
  }, [userId])

  return state
}

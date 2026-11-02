import type { ReactNode } from 'react'
import { GameLoadingRouteContext } from './gameLoadingContext'
import type { GameLoadingEntry } from './types'
import { GameExperienceRouteProvider } from '../game-experience/GameExperienceRouteProvider'

/** A route supplies presentation data without coupling pending views to routing. */
export function GameLoadingRouteProvider({ entry, pathname, children }: { entry?: GameLoadingEntry; pathname?: string; children: ReactNode }) {
  return <GameLoadingRouteContext.Provider value={entry}>
    <GameExperienceRouteProvider pathname={pathname} fallbackGameId={entry?.id}>{children}</GameExperienceRouteProvider>
  </GameLoadingRouteContext.Provider>
}

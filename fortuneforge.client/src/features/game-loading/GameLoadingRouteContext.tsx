import type { ReactNode } from 'react'
import { GameLoadingRouteContext } from './gameLoadingContext'
import type { GameLoadingEntry } from './types'

/** A route supplies presentation data without coupling pending views to routing. */
export function GameLoadingRouteProvider({ entry, children }: { entry?: GameLoadingEntry; children: ReactNode }) {
  return <GameLoadingRouteContext.Provider value={entry}>{children}</GameLoadingRouteContext.Provider>
}

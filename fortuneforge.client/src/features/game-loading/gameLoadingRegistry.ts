import { LOADING_PRESENTATIONS_A } from './loadingPresentationsA'
import { LOADING_PRESENTATIONS_B } from './loadingPresentationsB'
import { LOADING_PRESENTATIONS_C } from './loadingPresentationsC'
import type { GameLoadingEntry } from './types'

/** Presentation data only; neither this registry nor its entries load playable games. */
export const GAME_LOADING_PRESENTATIONS: readonly GameLoadingEntry[] = Object.freeze([
  ...LOADING_PRESENTATIONS_A,
  ...LOADING_PRESENTATIONS_B,
  ...LOADING_PRESENTATIONS_C,
])

const presentationsByRoute = new Map<string, GameLoadingEntry>()
for (const entry of GAME_LOADING_PRESENTATIONS) {
  for (const route of entry.routes) {
    if (presentationsByRoute.has(route)) throw new Error(`Duplicate game-loading route '${route}'.`)
    presentationsByRoute.set(route, entry)
  }
}

/** Resolve only registered aliases; the caller owns routing and pending-state lifetime. */
export function resolveGameLoadingEntry(pathname: string): GameLoadingEntry | undefined {
  const route = pathname.split(/[?#]/, 1)[0].replace(/\/+$/, '')
  return presentationsByRoute.get(route)
}

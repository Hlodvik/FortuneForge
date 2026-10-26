import { createContext, useContext } from 'react'
import type { GameLoadingEntry } from './types'

export const GameLoadingRouteContext = createContext<GameLoadingEntry | undefined>(undefined)

export function useGameLoadingEntry(): GameLoadingEntry | undefined {
  return useContext(GameLoadingRouteContext)
}

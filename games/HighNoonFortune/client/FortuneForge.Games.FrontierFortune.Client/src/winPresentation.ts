import type { FrontierState, FrontierWin, HandCategory } from './types.js'

export type FrontierWinPresentation = {
  id: string
  revision: number
  kind: 'lines' | 'hand'
  points: number
  wins: FrontierWin[]
  category: HandCategory | null
}

type WinState = Pick<FrontierState, 'revision' | 'lastWinPoints' | 'wins' | 'handRound' | 'pendingOperation'>

/** Presents only an already-settled server award. It never evaluates or credits a win. */
export function settledFrontierWin(state: WinState, clearedThroughRevision: number | null = null): FrontierWinPresentation | null {
  if (state.pendingOperation || state.lastWinPoints <= 0 || clearedThroughRevision !== null && state.revision <= clearedThroughRevision) return null
  const hand = state.handRound
  if (hand) {
    if (hand.phase !== 'complete' || hand.awardPoints !== state.lastWinPoints) return null
    return { id: `hand:${hand.roundId}`, revision: state.revision, kind: 'hand', points: state.lastWinPoints, wins: [], category: hand.category }
  }
  if (state.wins.length === 0) return null
  return { id: `lines:${state.revision}`, revision: state.revision, kind: 'lines', points: state.lastWinPoints, wins: state.wins, category: null }
}

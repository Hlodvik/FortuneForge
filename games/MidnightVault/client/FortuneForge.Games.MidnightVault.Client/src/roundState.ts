import { midnightCollections } from './manifest.js'
import type { MidnightDemoState, MidnightState } from './types.js'

export const emptyMidnightCollections = () => midnightCollections.map(({ id }) => ({
  sealId: id, count: 0, averageWagerPoints: 0, requiredCount: 30,
}))

export const emptyMidnightState = (): MidnightState => ({
  bonusRound: null,
  queuedBonusRounds: [],
  freeSpinsRemaining: 0,
  freeSpinWagerPoints: null,
  freeSpinFeatureMode: null,
  specialPointsBalance: 0,
  energyBalance: 0,
  sealCollections: emptyMidnightCollections(),
})

export const emptyMidnightDemoState = (): MidnightDemoState => ({
  rounds: [],
  collectionCounts: { sync: 0, rows: 0, paw: 0, rand: 0 },
  collectionWagerPoints: { sync: 0, rows: 0, paw: 0, rand: 0 },
})

export function activeMidnightWager(state: MidnightState, selectedWager: number) {
  return state.bonusRound?.wagerPoints ?? state.freeSpinWagerPoints ?? selectedWager
}

export function midnightRows(state: MidnightState) {
  return state.freeSpinsRemaining > 0 && state.freeSpinFeatureMode?.split('-').some(mode => mode.trim() === 'rows') ? 6 : 4
}

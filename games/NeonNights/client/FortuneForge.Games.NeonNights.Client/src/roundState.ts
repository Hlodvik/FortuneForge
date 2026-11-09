import type { NeonState, NeonTriggerSymbol } from './types.js'

export const emptyNeonState: NeonState = {
  freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null, neonDiscoSpinsRemaining: 0,
}
export function getNeonWager(state: NeonState, chosenWager: number) {
  return state.freeSpinsRemaining > 0 ? state.freeSpinWagerPoints ?? chosenWager : chosenWager
}
export function getNeonCharge(state: NeonState, chosenWager: number) {
  return state.freeSpinsRemaining > 0 && state.freeSpinFeatureMode !== 'arcade-rows' ? 0 : getNeonWager(state, chosenWager)
}
export function getNeonRows(state: NeonState) {
  return state.freeSpinsRemaining > 0 && state.freeSpinFeatureMode === 'arcade-rows' ? 7 : 4
}
export function previewRound(symbol: NeonTriggerSymbol, wager: number): NeonState {
  return symbol === 'POWER' ? { ...emptyNeonState, neonDiscoSpinsRemaining: 10 } : {
    ...emptyNeonState, freeSpinsRemaining: 7, freeSpinWagerPoints: wager,
    freeSpinFeatureMode: symbol === 'SEAL_RAND' ? 'arcade-rows' : null,
  }
}

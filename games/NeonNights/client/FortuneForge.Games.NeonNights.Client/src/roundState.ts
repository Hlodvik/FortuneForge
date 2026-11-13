import { discoMultipliers, type NeonDiscoMultiplier, type NeonState, type NeonTriggerSymbol } from './types.js'

export const neonDiscoRoundSpins = 12
export const neonCarPlayMode = 'car-rows'

export const emptyNeonState: NeonState = {
  freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
  neonDiscoSpinsRemaining: 0, neonDiscoMultiplier: 1,
}
export function stepDiscoMultiplier(multiplier: NeonDiscoMultiplier, direction: -1 | 1): NeonDiscoMultiplier {
  const index = discoMultipliers.indexOf(multiplier) + direction
  return discoMultipliers[Math.max(0, Math.min(index, discoMultipliers.length - 1))]
}
export function getNeonWager(state: NeonState, chosenWager: number) {
  return state.freeSpinsRemaining > 0 ? state.freeSpinWagerPoints ?? chosenWager : chosenWager
}
export function isCarPlayMode(mode: string | null) {
  return mode === neonCarPlayMode || mode === 'arcade-rows'
}
export function getNeonCharge(state: NeonState, chosenWager: number) {
  return state.freeSpinsRemaining > 0 && !isCarPlayMode(state.freeSpinFeatureMode) ? 0 : getNeonWager(state, chosenWager)
}
export function getNeonRows(state: NeonState) {
  return state.freeSpinsRemaining > 0 && isCarPlayMode(state.freeSpinFeatureMode) ? 7 : 4
}
export function previewRound(symbol: NeonTriggerSymbol, wager: number): NeonState {
  return symbol === 'POWER' ? { ...emptyNeonState, neonDiscoSpinsRemaining: neonDiscoRoundSpins } : {
    ...emptyNeonState, freeSpinsRemaining: 7, freeSpinWagerPoints: wager,
    freeSpinFeatureMode: neonCarPlayMode,
  }
}

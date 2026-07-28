import type { SlotResultSoundEvent } from '../config/soundSets'
import type { PaylinePayout } from '../types/slots'

// Presentation chooses one payline to highlight without changing payout math.
export function findBestPayline(paylines: readonly PaylinePayout[]): PaylinePayout | null {
  return [...paylines]
    .sort((left, right) =>
      right.amountPoints - left.amountPoints ||
      Math.max(0, ...right.matches.map((match) => match.match.matchLength)) -
        Math.max(0, ...left.matches.map((match) => match.match.matchLength)) ||
      Math.max(0, ...right.matches.map((match) => match.multiplier)) -
        Math.max(0, ...left.matches.map((match) => match.multiplier)) ||
      left.paylineId - right.paylineId,
    )[0] ?? null
}

// Translate a winning payline into a semantic sound event. The selected sound
// set decides which concrete WAV cues that event plays.
export function selectWinSoundEvent(
  payline: PaylinePayout | null,
  reelCount: number,
): SlotResultSoundEvent | null {
  if (payline === null) {
    return null
  }

  const hasFiveInARow = payline.matches.some(
    ({ match }) => match.matchLength === reelCount,
  )
  const threeInARowCount = payline.matches.filter(
    ({ match }) => match.matchLength === 3,
  ).length
  if (hasFiveInARow) {
    return 'five'
  }
  if (threeInARowCount >= 2) {
    return 'premium'
  }
  return threeInARowCount === 1 ? 'single-three' : null
}

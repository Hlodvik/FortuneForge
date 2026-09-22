import type { SlotResultSoundEvent } from '../config/soundSets'
import {
  DEFAULT_SLOT_OUTCOME_NARRATIVE,
  type SlotOutcomeNarrative,
} from '../config/outcomeNarratives'
import type { PaylinePayout } from '../types/slots'

export type SlotWinTier = 'regular' | 'great' | 'big'

export type SlotSpinOutcome = {
  kind: 'loss' | 'win' | 'great-win' | 'big-win' | 'bonus'
  title: string
  awardRand: number
  freeSpinsAwarded: number
  nextAction: string
  winTier: SlotWinTier | null
}

const bigWinMinimumRand = 500
const bigWinMultiplier = 50
const greatWinMultiplier = 10

export type WinPresentationTier = 'win' | 'big' | 'jackpot'

export function getWinPresentationTier(amount: number, wager: number): WinPresentationTier {
  if (amount >= Math.max(500, wager * 50)) return 'jackpot'
  return amount >= Math.max(25, wager * 10) ? 'big' : 'win'
}

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
  const shortMatchCount = payline.matches.filter(
    ({ match }) => match.matchLength >= 3 && match.matchLength < reelCount,
  ).length
  if (hasFiveInARow) {
    return 'five'
  }
  if (shortMatchCount >= 2) {
    return 'premium'
  }
  return shortMatchCount === 1 ? 'single-three' : null
}

// Celebration tiers are strictly presentation rules. They interpret a result
// that has already been settled and never influence its payout.
export function getSlotWinTier(awardRand: number, wagerRand: number): SlotWinTier {
  const safeAward = Math.max(0, awardRand)
  const safeWager = Math.max(0, wagerRand)

  if (safeAward >= Math.max(bigWinMinimumRand, safeWager * bigWinMultiplier)) {
    return 'big'
  }
  if (safeWager > 0 && safeAward >= safeWager * greatWinMultiplier) {
    return 'great'
  }
  return 'regular'
}

export function describeSpinOutcome({
  awardRand,
  wagerRand,
  freeSpinsAwarded,
  narrative = DEFAULT_SLOT_OUTCOME_NARRATIVE,
}: {
  awardRand: number
  wagerRand: number
  freeSpinsAwarded: number
  narrative?: SlotOutcomeNarrative
}): SlotSpinOutcome {
  const safeAward = Math.max(0, awardRand)
  const safeFreeSpins = Math.max(0, Math.floor(freeSpinsAwarded))
  const winTier = safeAward > 0 ? getSlotWinTier(safeAward, wagerRand) : null

  if (safeFreeSpins > 0) {
    return {
      kind: 'bonus',
      title: `${safeFreeSpins} ${safeFreeSpins === 1
        ? narrative.freeGameSingular
        : narrative.freeGamePlural} won`,
      awardRand: safeAward,
      freeSpinsAwarded: safeFreeSpins,
      nextAction: narrative.bonusNextAction,
      winTier,
    }
  }

  if (safeAward <= 0) {
    return {
      kind: 'loss',
      title: narrative.lossTitle,
      awardRand: 0,
      freeSpinsAwarded: 0,
      nextAction: narrative.lossNextAction,
      winTier: null,
    }
  }

  const kind = winTier === 'big'
    ? 'big-win'
    : winTier === 'great'
      ? 'great-win'
      : 'win'
  const title = winTier === 'big'
    ? narrative.bigWinTitle
    : winTier === 'great'
      ? narrative.greatWinTitle
      : narrative.winTitle

  return {
    kind,
    title,
    awardRand: safeAward,
    freeSpinsAwarded: 0,
    nextAction: narrative.winNextAction,
    winTier,
  }
}

export function selectOutcomeSoundEvent(
  payline: PaylinePayout | null,
  reelCount: number,
  winTier: SlotWinTier | null,
): SlotResultSoundEvent | null {
  if (winTier === 'big') {
    return 'five'
  }
  if (winTier === 'great') {
    return 'premium'
  }
  return selectWinSoundEvent(payline, reelCount)
}

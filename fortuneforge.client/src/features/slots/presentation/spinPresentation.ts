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

export const bigWinMinimumRand = 500
export const bigWinMultiplier = 50
const greatWinMultiplier = 10

export type WinPresentationTier = 'win' | 'big' | 'jackpot'

export function getWinPresentationTier(amount: number, wager: number): WinPresentationTier {
  if (amount >= Math.max(500, wager * 50)) return 'jackpot'
  return amount >= Math.max(25, wager * 10) ? 'big' : 'win'
}

export type SlotOutcomePresentation = Readonly<{
  detail?: string
  label: string
  nextAction: string
  significance: 'standard' | 'major'
  title: string
  tone: 'win' | 'milestone' | 'neutral' | 'loss'
}>

export type SlotOutcomePresentationInput = Readonly<{
  activeWager: number
  bestPayline: PaylinePayout | null
  bestSymbolLabel: string | null
  canAffordSelectedWager: boolean
  demoAvailabilityMessage: string | null
  energyLabel: string | null
  gameTitle: string
  hasCompletedSpin: boolean
  isSpinning: boolean
  lastEnergyAwarded: number
  lastEnergyMultiplierApplied: boolean
  lastFreeSpinsAwarded: number
  lastWin: number
  spinError: string | null
  spinStage: 'requesting' | 'stopping'
  freeSpinsRemaining: number
  useFreeGameForNextSpin: boolean
}>

export function isBigWinAward(award: number, wager: number): boolean {
  return award >= Math.max(bigWinMinimumRand, wager * bigWinMultiplier)
}

/**
 * Keeps a result's explanation separate from the result itself. This is UI
 * copy only: it reads values already calculated by the game and never changes
 * a spin, balance, payout, or feature state.
 */
export function getSlotOutcomePresentation({
  activeWager,
  bestPayline,
  bestSymbolLabel,
  canAffordSelectedWager,
  demoAvailabilityMessage,
  energyLabel,
  gameTitle,
  hasCompletedSpin,
  isSpinning,
  lastEnergyAwarded,
  lastEnergyMultiplierApplied,
  lastFreeSpinsAwarded,
  lastWin,
  spinError,
  spinStage,
  freeSpinsRemaining,
  useFreeGameForNextSpin,
}: SlotOutcomePresentationInput): SlotOutcomePresentation {
  if (demoAvailabilityMessage !== null) {
    return {
      label: 'Game unavailable',
      title: 'The game service is offline',
      detail: demoAvailabilityMessage,
      nextAction: 'Reload after the service is restored.',
      significance: 'standard',
      tone: 'loss',
    }
  }

  if (spinError !== null) {
    return {
      label: 'Spin needs attention',
      title: 'No new result was recorded',
      detail: spinError,
      nextAction: 'Review the message, then choose a valid wager or try again.',
      significance: 'standard',
      tone: 'loss',
    }
  }

  if (isSpinning) {
    return {
      label: 'Spin in progress',
      title: `${gameTitle} is ${spinStage === 'requesting' ? 'spinning' : 'landing'}`,
      detail: spinStage === 'requesting'
        ? 'The reels are in motion.'
        : 'The result is being revealed.',
      nextAction: 'Press Spin again if you want the reels to stop early.',
      significance: 'standard',
      tone: 'neutral',
    }
  }

  if (lastFreeSpinsAwarded > 0) {
    const noun = lastFreeSpinsAwarded === 1 ? 'free game' : 'free games'
    const remainingNoun = freeSpinsRemaining === 1 ? 'free game is' : 'free games are'
    return {
      label: 'Feature unlocked',
      title: `${lastFreeSpinsAwarded} ${noun} won`,
      detail: `${freeSpinsRemaining} ${remainingNoun} ready at ${formatOutcomeRand(activeWager)}.`,
      nextAction: 'Press Spin to begin your free games.',
      significance: 'major',
      tone: 'milestone',
    }
  }

  if (lastEnergyMultiplierApplied) {
    return {
      label: 'Boosted result',
      title: `${energyLabel ?? 'Energy'} boost ×1.5${lastWin > 0 ? ` · +${formatOutcomeRand(lastWin)}` : ''}`,
      detail: `${energyLabel ?? 'The meter'} reset after applying the boost to this spin.`,
      nextAction: 'Spin again to start charging the meter.',
      significance: 'major',
      tone: 'milestone',
    }
  }

  if (lastEnergyAwarded > 0) {
    return {
      label: 'Meter progress',
      title: `${energyLabel ?? 'Energy'} collected`,
      detail: `+${lastEnergyAwarded} added to the ${energyLabel?.toLowerCase() ?? 'energy'} meter.`,
      nextAction: 'Keep spinning to build the next boost.',
      significance: 'standard',
      tone: 'milestone',
    }
  }

  if (lastWin > 0) {
    const bestMatch = bestPayline?.matches.reduce((best, candidate) => (
      candidate.amountPoints > best.amountPoints ? candidate : best
    ))
    const matchDetail = bestMatch
      ? `Best line: ${bestSymbolLabel ?? 'matching symbols'} ×${bestMatch.match.matchLength} on line ${bestPayline?.paylineId ?? 0}.`
      : 'Winning symbols are highlighted on the reels.'
    const bigWin = isBigWinAward(lastWin, activeWager)
    return {
      label: bigWin ? 'Big win' : 'Line win',
      title: `+${formatOutcomeRand(lastWin)}`,
      detail: matchDetail,
      nextAction: 'Spin again, or adjust your wager before the next result.',
      significance: bigWin ? 'major' : 'standard',
      tone: 'win',
    }
  }

  if (hasCompletedSpin) {
    if (useFreeGameForNextSpin) {
      return {
        label: 'Free game ready',
        title: `${freeSpinsRemaining} free ${freeSpinsRemaining === 1 ? 'game' : 'games'} remain`,
        detail: `The free-game wager stays locked at ${formatOutcomeRand(activeWager)}.`,
        nextAction: 'Press Spin for the next free game.',
        significance: 'standard',
        tone: 'milestone',
      }
    }
    return {
      label: 'No line win',
      title: 'The reels are ready for the next spin',
      detail: `This ${formatOutcomeRand(activeWager)} spin did not land a paying line.`,
      nextAction: 'Spin again or adjust your wager.',
      significance: 'standard',
      tone: 'neutral',
    }
  }

  if (!canAffordSelectedWager) {
    return {
      label: 'Wager selection',
      title: 'Choose a smaller wager',
      detail: `${formatOutcomeRand(activeWager)} is above the available balance.`,
      nextAction: 'Use the minus control, then press Spin.',
      significance: 'standard',
      tone: 'loss',
    }
  }

  return {
    label: 'Wager selected',
    title: `${formatOutcomeRand(activeWager)} wager`,
    detail: 'Paying symbols and active features are explained after each result.',
    nextAction: 'Press Spin when you are ready.',
    significance: 'standard',
    tone: 'neutral',
  }
}

function formatOutcomeRand(amount: number): string {
  return `R${new Intl.NumberFormat('en-US').format(amount)}`
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

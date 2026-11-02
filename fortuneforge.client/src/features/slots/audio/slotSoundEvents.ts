import type { SlotResultSoundEvent, SlotSoundCueId, SlotSoundSet } from '../config/soundSets'
import type { SpinResult } from '../types/slots'

/** Monetary/event inspection only; no outcome, wager or feature state changes. */
export function getSuiteResultCue(soundSet: SlotSoundSet, result: SpinResult,
  event: SlotResultSoundEvent | null, expectedFreeSpin: boolean, newlyAwardedSpins: number): SlotSoundCueId | undefined {
  if (!soundSet.events.themeSuite) return undefined
  if (expectedFreeSpin && result.isFreeSpin && result.freeSpinsRemaining === 0 && newlyAwardedSpins === 0 && soundSet.events.bonusComplete)
    return soundSet.events.bonusComplete
  if (event === 'bonus') return soundSet.events.results.bonus[0]
  // Preserve major-win priority; a standalone paid777 pattern has its own cue.
  if (event !== 'five' && event !== 'premium' && result.bananaBonusPoints > 0 && soundSet.events.tripleSevens)
    return soundSet.events.tripleSevens
  if (event === null) return undefined
  return (expectedFreeSpin ? soundSet.events.specialResults?.[event] : undefined)?.[0] ?? soundSet.events.results[event][0]
}

export function getCollectionArrivalCue(soundSet: SlotSoundSet, track: string): SlotSoundCueId | undefined {
  if (!soundSet.events.themeSuite || !['sync', 'rows', 'paw', 'rand'].includes(track)) return undefined
  return soundSet.events.collectionArrival?.[track as 'sync' | 'rows' | 'paw' | 'rand']
}

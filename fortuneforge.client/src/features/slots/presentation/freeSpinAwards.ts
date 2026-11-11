import type { SpinResult } from '../types/slots'

/** Presentation only: some demo responses put collection awards in the balance. */
export function getPresentedFreeSpinsAwarded(
  result: Pick<SpinResult, 'freeSpinsAwarded' | 'freeSpinsRemaining'>,
  previousRemaining: number,
  consumedFreeSpin: boolean,
): number {
  const reported = Number.isSafeInteger(result.freeSpinsAwarded) && result.freeSpinsAwarded >= 0
    ? result.freeSpinsAwarded : 0
  if (!Number.isSafeInteger(previousRemaining) || previousRemaining < 0 ||
      !Number.isSafeInteger(result.freeSpinsRemaining) || result.freeSpinsRemaining < 0) return reported
  const delta = result.freeSpinsRemaining - previousRemaining + (consumedFreeSpin ? 1 : 0)
  return Number.isSafeInteger(delta) ? Math.max(reported, delta, 0) : reported
}

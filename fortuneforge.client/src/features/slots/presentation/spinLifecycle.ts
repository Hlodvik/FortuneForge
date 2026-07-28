import type { ReelMotionState } from '../animation/slotAnimation'

export const neutralAccelerationDurationMs = 150
export const minimumSpinDurationMs = 420
export const spinLoopDurationMs = 192
export const maximumSpinBoundaryWaitMs = spinLoopDurationMs - 12
export const normalReelStopStaggerMs = 110
export const quickReelStopStaggerMs = 28
export const normalBrakeDurationMs = 240
export const quickBrakeDurationMs = 90
export const normalLandingDurationMs = 110
export const quickLandingDurationMs = 44

export class SlotStateRevisionGuard {
  private revision = 0

  capture(): number {
    return this.revision
  }

  advance(): number {
    this.revision += 1
    return this.revision
  }

  isCurrent(capturedRevision: number): boolean {
    return capturedRevision === this.revision
  }
}

export function shouldUseAnimatedSymbol(
  prefersReducedMotion: boolean,
  reelMotion: ReelMotionState,
): boolean {
  return !prefersReducedMotion && (reelMotion === 'idle' || reelMotion === 'stopped')
}

export function getReelStopOffsetMs(
  reelIndex: number,
  speedMultiplier: number,
  quickStop: boolean,
): number {
  const safeIndex = Math.max(0, Math.floor(reelIndex))
  if (quickStop) {
    return safeIndex * quickReelStopStaggerMs
  }

  return safeIndex * normalReelStopStaggerMs / Math.max(1, speedMultiplier)
}

export function getLatestReelSettleBudgetMs(
  reelCount: number,
  speedMultiplier: number,
  quickStop: boolean,
): number {
  const finalReelIndex = Math.max(0, Math.floor(reelCount) - 1)
  const safeSpeedMultiplier = Math.max(1, speedMultiplier)
  const brakeDuration = quickStop
    ? quickBrakeDurationMs
    : normalBrakeDurationMs / safeSpeedMultiplier
  const landingDuration = quickStop
    ? quickLandingDurationMs
    : normalLandingDurationMs / safeSpeedMultiplier

  return getReelStopOffsetMs(finalReelIndex, safeSpeedMultiplier, quickStop)
    + brakeDuration
    + landingDuration
}

export function waitForPresentation(
  durationMs: number,
  signal: AbortSignal,
): Promise<boolean> {
  if (signal.aborted || durationMs <= 0) {
    return Promise.resolve(!signal.aborted)
  }

  return new Promise((resolve) => {
    let settled = false
    const finish = (completed: boolean) => {
      if (settled) {
        return
      }
      settled = true
      globalThis.clearTimeout(timer)
      signal.removeEventListener('abort', handleAbort)
      resolve(completed)
    }
    const handleAbort = () => finish(false)
    const timer = globalThis.setTimeout(() => finish(true), durationMs)
    signal.addEventListener('abort', handleAbort, { once: true })
  })
}

export function waitForPresentationFrame(signal: AbortSignal): Promise<boolean> {
  if (signal.aborted) {
    return Promise.resolve(false)
  }

  return new Promise((resolve) => {
    let settled = false
    const finish = (completed: boolean) => {
      if (settled) {
        return
      }
      settled = true
      window.cancelAnimationFrame(frameId)
      signal.removeEventListener('abort', handleAbort)
      resolve(completed)
    }
    const handleAbort = () => finish(false)
    const frameId = window.requestAnimationFrame(() => finish(true))
    signal.addEventListener('abort', handleAbort, { once: true })
  })
}

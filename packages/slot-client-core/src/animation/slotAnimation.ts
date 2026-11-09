import {
  minimumSpinDurationMs,
  neutralAccelerationDurationMs,
  normalBrakeDurationMs,
  normalReelStopStaggerMs,
  quickReelStopStaggerMs,
  spinLoopDurationMs,
} from '../presentation/spinLifecycle'

export type ReelMotionState =
  | 'idle'
  | 'accelerating'
  | 'spinning'
  | 'braking'
  | 'stopped'

type StartSpinAnimationOptions<TSymbol extends string = string> = {
  reelCount: number
  rowsPerReel: number
  displayFrame: (reelIndex: number, symbols: readonly TSymbol[]) => void
  reducedMotion?: boolean
  setReelMotion: (reelIndex: number, state: ReelMotionState) => void
  speedMultiplier?: number
  symbolIds: readonly TSymbol[]
  travelRows?: number
}

type StopReelAnimationOptions<TSymbol extends string = string> = {
  isQuickStopRequested?: () => boolean
  reelIndex: number
  onStopped?: () => void
  quickBrakeDurationMs?: number
  quickSettleAfterStopMs?: number
  settleAfterStopMs?: number
  skipMinimumDuration?: boolean
  targetSymbols: readonly TSymbol[]
}

type StopSpinAnimationWithCadenceOptions<TSymbol extends string = string> = {
  animation: SpinAnimation<TSymbol>
  isQuickStopRequested: () => boolean
  onReelStopped?: (reelIndex: number, stoppedReelCount: number) => void
  quickBrakeDurationMs?: number
  quickSettleAfterStopMs?: number
  settleAfterLastReelMs?: number
  shouldSnapToTarget: () => boolean
  snapToTarget: () => void
  targetReels: readonly (readonly TSymbol[])[]
}

export type SpinAnimation<TSymbol extends string = string> = {
  readonly activeReels: boolean[]
  readonly displayFrame: StartSpinAnimationOptions<TSymbol>['displayFrame']
  readonly frameOffsets: number[]
  readonly reducedMotion: boolean
  readonly rowsPerReel: number
  readonly setReelMotion: StartSpinAnimationOptions<TSymbol>['setReelMotion']
  readonly speedMultiplier: number
  readonly startTimers: number[]
  readonly symbolIds: readonly TSymbol[]
  readonly travelRows: number
  finished: boolean
  quickStopStartedAt: number | null
  spinStartedAt: number
}

const defaultTravelRows = 8
export const reelLandingSettleDurationMs = 110

export function startSpinAnimation<TSymbol extends string>(options: StartSpinAnimationOptions<TSymbol>): SpinAnimation<TSymbol> {
  const reelCount = Math.max(1, Math.floor(options.reelCount))
  const rowsPerReel = Math.max(1, Math.floor(options.rowsPerReel))
  const reducedMotion = options.reducedMotion
    ?? window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const speedMultiplier = Math.max(1, options.speedMultiplier ?? 1)
  const travelRows = Math.max(1, Math.floor(options.travelRows ?? defaultTravelRows))
  const symbolIds: readonly TSymbol[] = options.symbolIds.length > 0
    ? [...options.symbolIds]
    : ['2' as TSymbol]
  const animation: SpinAnimation<TSymbol> = {
    activeReels: Array.from({ length: reelCount }, () => !reducedMotion),
    displayFrame: options.displayFrame,
    frameOffsets: Array.from({ length: reelCount }, (_, reelIndex) => reelIndex),
    reducedMotion,
    rowsPerReel,
    setReelMotion: options.setReelMotion,
    speedMultiplier,
    startTimers: [],
    symbolIds,
    travelRows,
    finished: false,
    quickStopStartedAt: null,
    spinStartedAt: performance.now(),
  }

  for (let reelIndex = 0; reelIndex < reelCount; reelIndex++) {
    options.setReelMotion(reelIndex, reducedMotion ? 'idle' : 'accelerating')

    if (reducedMotion) {
      continue
    }

    options.displayFrame(
      reelIndex,
      buildFrame(
        animation.symbolIds,
        animation.frameOffsets[reelIndex],
        rowsPerReel + travelRows,
      ),
    )
    animation.startTimers.push(window.setTimeout(() => {
      if (!animation.finished && animation.activeReels[reelIndex]) {
        options.setReelMotion(reelIndex, 'spinning')
      }
    }, neutralAccelerationDurationMs / speedMultiplier))
  }

  if (!reducedMotion) {
    window.requestAnimationFrame((timestamp) => {
      animation.spinStartedAt = timestamp
    })
  }

  return animation
}

export async function prepareSpinForStopping<TSymbol extends string>(
  animation: SpinAnimation<TSymbol>,
  isQuickStopRequested: () => boolean,
): Promise<void> {
  if (animation.finished || animation.reducedMotion) {
    return
  }

  const shouldEndEarly = () =>
    animation.finished || isQuickStopRequested()
  const minimumSpinRemainingMs = shouldEndEarly()
    ? 0
    : Math.max(
        0,
        minimumSpinDurationMs / animation.speedMultiplier -
          (performance.now() - animation.spinStartedAt),
      )
  if (minimumSpinRemainingMs > 0) {
    await waitUntil(minimumSpinRemainingMs, shouldEndEarly)
  }

  if (!animation.finished) {
    await waitForSpinBoundary(animation, shouldEndEarly)
  }
}

export async function waitForReelStopOffset<TSymbol extends string>(
  animation: SpinAnimation<TSymbol>,
  reelIndex: number,
  isQuickStopRequested: () => boolean,
): Promise<void> {
  if (animation.finished || reelIndex <= 0) {
    return
  }

  if (isQuickStopRequested()) {
    await waitForRemainingQuickStopOffset(animation, reelIndex)
    return
  }

  const interrupted = await waitUntil(
    reelIndex * normalReelStopStaggerMs / animation.speedMultiplier,
    () => animation.finished || isQuickStopRequested(),
  )
  if (interrupted && !animation.finished) {
    await waitForRemainingQuickStopOffset(animation, reelIndex)
  }
}

export function requestQuickStop<TSymbol extends string>(animation: SpinAnimation<TSymbol>): void {
  if (animation.quickStopStartedAt === null) {
    animation.quickStopStartedAt = performance.now()
  }
}

export async function stopSpinAnimationWithCadence<TSymbol extends string>(
  options: StopSpinAnimationWithCadenceOptions<TSymbol>,
): Promise<void> {
  const {
    animation,
    isQuickStopRequested,
    shouldSnapToTarget,
    snapToTarget,
    targetReels,
  } = options

  if (animation.reducedMotion || shouldSnapToTarget()) {
    snapToTarget()
    return
  }

  await prepareSpinForStopping(animation, isQuickStopRequested)
  if (animation.finished) {
    if (shouldSnapToTarget()) {
      snapToTarget()
    }
    return
  }

  let stoppedReelCount = 0
  await Promise.all(targetReels.map(async (_, reelIndex) => {
    await waitForReelStopOffset(
      animation,
      reelIndex,
      isQuickStopRequested,
    )
    await stopReelAnimation(animation, {
      isQuickStopRequested,
      reelIndex,
      onStopped: () => {
        stoppedReelCount += 1
        options.onReelStopped?.(reelIndex, stoppedReelCount)
      },
      quickBrakeDurationMs: options.quickBrakeDurationMs,
      quickSettleAfterStopMs: options.quickSettleAfterStopMs,
      settleAfterStopMs:
        reelIndex === targetReels.length - 1
          ? options.settleAfterLastReelMs
          : 0,
      skipMinimumDuration: true,
      targetSymbols: targetReels[reelIndex],
    })
  }))

  if (shouldSnapToTarget()) {
    snapToTarget()
  }
}

export async function stopReelAnimation<TSymbol extends string>(
  animation: SpinAnimation<TSymbol>,
  options: StopReelAnimationOptions<TSymbol>,
): Promise<void> {
  const isQuickStopRequested = () => options.isQuickStopRequested?.() === true
  const getSettleAfterStopMs = () => isQuickStopRequested()
    ? options.quickSettleAfterStopMs ?? Math.min(options.settleAfterStopMs ?? 0, 40)
    : options.settleAfterStopMs ?? 0

  if (animation.finished) {
    return
  }

  if (animation.reducedMotion) {
    animation.displayFrame(options.reelIndex, options.targetSymbols)
    animation.setReelMotion(options.reelIndex, 'stopped')
    options.onStopped?.()
    await waitForPaint()
    await wait(getSettleAfterStopMs())
    return
  }

  if (!options.skipMinimumDuration) {
    await prepareSpinForStopping(animation, isQuickStopRequested)
  }
  if (animation.finished) {
    return
  }
  animation.activeReels[options.reelIndex] = false

  const brakingStrip = [
    ...buildFrame(
      animation.symbolIds,
      animation.frameOffsets[options.reelIndex],
      animation.travelRows,
    ),
    ...options.targetSymbols,
  ]
  animation.displayFrame(options.reelIndex, brakingStrip)
  animation.setReelMotion(options.reelIndex, 'braking')
  if (isQuickStopRequested()) {
    await wait(options.quickBrakeDurationMs ?? 90)
  } else {
    const interrupted = await waitUntil(
      normalBrakeDurationMs / animation.speedMultiplier,
      () => animation.finished || isQuickStopRequested(),
    )
    if (interrupted && !animation.finished) {
      await wait(options.quickBrakeDurationMs ?? 90)
    }
  }

  if (animation.finished) {
    return
  }
  animation.displayFrame(options.reelIndex, options.targetSymbols)
  animation.setReelMotion(options.reelIndex, 'stopped')
  options.onStopped?.()
  await waitForPaint()
  await wait(getSettleAfterStopMs())
}

async function waitForSpinBoundary<TSymbol extends string>(
  animation: SpinAnimation<TSymbol>,
  shouldEndEarly?: () => boolean,
): Promise<void> {
  if (shouldEndEarly?.()) {
    return
  }

  const elapsedMs = Math.max(0, performance.now() - animation.spinStartedAt)
  const effectiveLoopDurationMs = spinLoopDurationMs / animation.speedMultiplier
  const elapsedInLoopMs = elapsedMs % effectiveLoopDurationMs
  const remainingMs = elapsedInLoopMs < 8
    ? 0
    : effectiveLoopDurationMs - elapsedInLoopMs
  await waitUntil(Math.max(0, remainingMs - 12), shouldEndEarly)
}

export function finishSpinAnimation<TSymbol extends string>(animation: SpinAnimation<TSymbol>): void {
  cancelSpinAnimation(animation)

  for (let reelIndex = 0; reelIndex < animation.activeReels.length; reelIndex++) {
    animation.setReelMotion(reelIndex, 'idle')
  }
}

export function cancelSpinAnimation<TSymbol extends string>(animation: SpinAnimation<TSymbol>): void {
  animation.finished = true
  animation.startTimers.forEach((timer) => window.clearTimeout(timer))
  animation.startTimers.length = 0

  for (let reelIndex = 0; reelIndex < animation.activeReels.length; reelIndex++) {
    animation.activeReels[reelIndex] = false
  }
}

function buildFrame<TSymbol extends string>(
  symbolIds: readonly TSymbol[],
  offset: number,
  rowCount: number,
): TSymbol[] {
  return Array.from(
    { length: rowCount },
    (_, rowIndex) => symbolIds[(offset + rowIndex) % symbolIds.length],
  )
}

async function waitForRemainingQuickStopOffset<TSymbol extends string>(
  animation: SpinAnimation<TSymbol>,
  reelIndex: number,
): Promise<void> {
  const quickStopStartedAt = animation.quickStopStartedAt ?? performance.now()
  animation.quickStopStartedAt ??= quickStopStartedAt
  const deadline =
    quickStopStartedAt +
    reelIndex * quickReelStopStaggerMs
  await waitUntil(
    Math.max(0, deadline - performance.now()),
    () => animation.finished,
  )
}

function wait(durationMs: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, durationMs))
}

async function waitUntil(
  durationMs: number,
  shouldEndEarly?: () => boolean,
): Promise<boolean> {
  const normalizedDurationMs = Math.max(0, durationMs)
  if (normalizedDurationMs === 0 || shouldEndEarly?.()) {
    return shouldEndEarly?.() === true
  }

  const deadline = performance.now() + normalizedDurationMs
  while (performance.now() < deadline) {
    if (shouldEndEarly?.()) {
      return true
    }

    await wait(Math.min(32, Math.max(0, deadline - performance.now())))
  }

  return shouldEndEarly?.() === true
}

function waitForPaint(): Promise<void> {
  return new Promise((resolve) => {
    window.requestAnimationFrame(() => resolve())
  })
}

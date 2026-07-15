import { slotSymbolIds, type SlotSymbolId } from '../types/slots'

export type ReelMotionState = 'idle' | 'spinning' | 'settling' | 'stopped'

type StartSpinAnimationOptions = {
  reelCount: number
  rowsPerReel: number
  displayFrame: (reelIndex: number, symbols: readonly SlotSymbolId[]) => void
  setReelMotion: (reelIndex: number, state: ReelMotionState) => void
}

type StopReelAnimationOptions = {
  reelIndex: number
  stopIndex: number
  targetSymbols: readonly SlotSymbolId[]
}

export type SpinAnimation = {
  readonly displayFrame: StartSpinAnimationOptions['displayFrame']
  readonly frameCounts: number[]
  readonly frameOffsets: number[]
  readonly intervals: Array<number | undefined>
  readonly reducedMotion: boolean
  readonly rowsPerReel: number
  readonly setReelMotion: StartSpinAnimationOptions['setReelMotion']
  finished: boolean
}

const frameDurationMs = 72
const minimumFramesPerReel = slotSymbolIds.length * 2
const landingFrameDurationsMs = [72, 105, 150] as const
const landingBounceDurationMs = 230

export function startSpinAnimation(options: StartSpinAnimationOptions): SpinAnimation {
  const reelCount = Math.max(1, Math.floor(options.reelCount))
  const rowsPerReel = Math.max(1, Math.floor(options.rowsPerReel))
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const animation: SpinAnimation = {
    displayFrame: options.displayFrame,
    frameCounts: Array.from({ length: reelCount }, () => 0),
    frameOffsets: Array.from({ length: reelCount }, (_, reelIndex) => reelIndex),
    intervals: Array.from({ length: reelCount }),
    reducedMotion,
    rowsPerReel,
    setReelMotion: options.setReelMotion,
    finished: false,
  }

  for (let reelIndex = 0; reelIndex < reelCount; reelIndex++) {
    options.setReelMotion(reelIndex, reducedMotion ? 'idle' : 'spinning')

    if (reducedMotion) {
      continue
    }

    animation.intervals[reelIndex] = window.setInterval(() => {
      animation.frameOffsets[reelIndex] += 1
      animation.frameCounts[reelIndex] += 1
      options.displayFrame(
        reelIndex,
        buildFrame(animation.frameOffsets[reelIndex], rowsPerReel),
      )
    }, frameDurationMs)
  }

  return animation
}

export async function stopReelAnimation(
  animation: SpinAnimation,
  options: StopReelAnimationOptions,
): Promise<void> {
  if (animation.finished) {
    return
  }

  if (animation.reducedMotion) {
    animation.displayFrame(options.reelIndex, options.targetSymbols)
    await waitForPaint()
    return
  }

  while (
    !animation.finished
    && animation.frameCounts[options.reelIndex] < minimumFramesPerReel
  ) {
    await wait(frameDurationMs)
  }

  clearReelInterval(animation, options.reelIndex)
  animation.setReelMotion(options.reelIndex, 'settling')

  const landingFrameCount = 2 + Math.abs(options.stopIndex % 2)
  for (let frameIndex = 0; frameIndex < landingFrameCount; frameIndex++) {
    animation.frameOffsets[options.reelIndex] += 1
    animation.displayFrame(
      options.reelIndex,
      buildFrame(animation.frameOffsets[options.reelIndex], animation.rowsPerReel),
    )
    await wait(landingFrameDurationsMs[frameIndex])
  }

  animation.displayFrame(options.reelIndex, options.targetSymbols)
  animation.setReelMotion(options.reelIndex, 'stopped')
  await waitForPaint()
  await wait(landingBounceDurationMs)
}

export function finishSpinAnimation(animation: SpinAnimation): void {
  animation.finished = true

  for (let reelIndex = 0; reelIndex < animation.intervals.length; reelIndex++) {
    clearReelInterval(animation, reelIndex)
    animation.setReelMotion(reelIndex, 'idle')
  }
}

function buildFrame(offset: number, rowCount: number): SlotSymbolId[] {
  return Array.from(
    { length: rowCount },
    (_, rowIndex) => slotSymbolIds[(offset + rowIndex) % slotSymbolIds.length],
  )
}

function clearReelInterval(animation: SpinAnimation, reelIndex: number): void {
  const interval = animation.intervals[reelIndex]
  if (interval !== undefined) {
    window.clearInterval(interval)
    animation.intervals[reelIndex] = undefined
  }
}

function wait(durationMs: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, durationMs))
}

function waitForPaint(): Promise<void> {
  return new Promise((resolve) => {
    window.requestAnimationFrame(() => resolve())
  })
}

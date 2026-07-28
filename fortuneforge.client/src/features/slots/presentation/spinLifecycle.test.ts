import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import {
  cancelSpinAnimation,
  requestQuickStop,
  startSpinAnimation,
  stopSpinAnimationWithCadence,
  type ReelMotionState,
} from '../animation/slotAnimation'
import { SpinButton } from '../components/SpinButton'
import type { SlotSymbolId } from '../types/slots'
import {
  SlotStateRevisionGuard,
  getLatestReelSettleBudgetMs,
  maximumSpinBoundaryWaitMs,
  minimumSpinDurationMs,
  shouldUseAnimatedSymbol,
  waitForPresentation,
} from './spinLifecycle'

const targetReels: SlotSymbolId[][] = [
  ['2', '3', '4', '5'],
  ['3', '4', '5', '6'],
  ['4', '5', '6', '7'],
  ['5', '6', '7', 'ACE'],
  ['6', '7', 'ACE', 'FREE'],
]

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

function installAnimationWindow() {
  vi.stubGlobal('window', {
    setTimeout: (...args: Parameters<typeof globalThis.setTimeout>) =>
      globalThis.setTimeout(...args),
    clearTimeout: (timer: ReturnType<typeof globalThis.setTimeout>) =>
      globalThis.clearTimeout(timer),
    requestAnimationFrame: (callback: FrameRequestCallback) =>
      globalThis.setTimeout(() => callback(performance.now()), 16),
    cancelAnimationFrame: (timer: ReturnType<typeof globalThis.setTimeout>) =>
      globalThis.clearTimeout(timer),
    matchMedia: () => ({ matches: false }),
  })
}

function startCadenceHarness() {
  let quickStopRequested = false
  let reducedMotionRequested = false
  let completedAt: number | null = null
  const displayedReels = targetReels.map((reel) => [...reel].reverse())
  const reelMotion: ReelMotionState[] = targetReels.map(() => 'idle')
  const animation = startSpinAnimation({
    reelCount: targetReels.length,
    rowsPerReel: targetReels[0].length,
    displayFrame: (reelIndex, symbols) => {
      displayedReels[reelIndex] = [...symbols]
    },
    reducedMotion: false,
    setReelMotion: (reelIndex, state) => {
      reelMotion[reelIndex] = state
    },
  })
  const snapToTarget = () => {
    targetReels.forEach((reel, reelIndex) => {
      displayedReels[reelIndex] = [...reel]
      reelMotion[reelIndex] = 'stopped'
    })
  }
  const completion = stopSpinAnimationWithCadence({
    animation,
    isQuickStopRequested: () => quickStopRequested,
    quickBrakeDurationMs: 90,
    quickSettleAfterStopMs: 35,
    settleAfterLastReelMs: 110,
    shouldSnapToTarget: () => reducedMotionRequested,
    snapToTarget,
    targetReels,
  }).then(() => {
    completedAt = performance.now()
  })

  return {
    animation,
    completion,
    displayedReels,
    reelMotion,
    getCompletedAt: () => completedAt,
    requestReducedMotion: () => {
      reducedMotionRequested = true
      cancelSpinAnimation(animation)
    },
    requestStop: () => {
      requestQuickStop(animation)
      quickStopRequested = true
    },
  }
}

describe('slot state revision guard', () => {
  it('prevents a deferred initial snapshot from overwriting a completed spin', async () => {
    const guard = new SlotStateRevisionGuard()
    const initialState = deferred<string>()
    const appliedStates: string[] = []
    const initialRevision = guard.capture()
    const hydrate = initialState.promise.then((state) => {
      if (guard.isCurrent(initialRevision)) {
        appliedStates.push(state)
      }
    })

    guard.advance()
    appliedStates.push('completed-spin')
    initialState.resolve('stale-initial-state')
    await hydrate

    expect(appliedStates).toEqual(['completed-spin'])
  })
})

describe('slot presentation timing', () => {
  it('keeps the worst-case normal reveal-to-settle budget below 1.4 seconds', () => {
    const reelSettleBudget = getLatestReelSettleBudgetMs(5, 1, false)
    const worstCaseBudget =
      minimumSpinDurationMs + maximumSpinBoundaryWaitMs + reelSettleBudget

    expect(reelSettleBudget).toBe(790)
    expect(worstCaseBudget).toBeLessThanOrEqual(1_400)
  })

  it('keeps the arithmetic five-reel quick-stop budget below 300ms', () => {
    expect(getLatestReelSettleBudgetMs(5, 1, true)).toBe(246)
  })

  it.each([
    ['minimum-spin wait', 100],
    ['reel-offset wait', 900],
    ['normal brake wait', 650],
  ])('settles the real five-reel cadence within 300ms when Stop interrupts %s', async (_, stopAtMs) => {
    vi.useFakeTimers()
    installAnimationWindow()
    const harness = startCadenceHarness()
    await vi.advanceTimersByTimeAsync(stopAtMs)
    const stopObservedAt = performance.now()

    harness.requestStop()
    await vi.advanceTimersByTimeAsync(300)
    await harness.completion

    expect(harness.getCompletedAt()).not.toBeNull()
    expect(harness.getCompletedAt()! - stopObservedAt).toBeLessThanOrEqual(300)
    expect(harness.displayedReels).toEqual(targetReels)
    cancelSpinAnimation(harness.animation)
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it.each([
    ['request wait', 420],
    ['win count', 1_250],
    ['award flyover', 540],
    ['energy delay', 280],
    ['energy flight', 680],
  ])('interrupts %s immediately and clears its timer', async (_, durationMs) => {
    vi.useFakeTimers()
    const controller = new AbortController()
    const pendingWait = waitForPresentation(durationMs, controller.signal)

    controller.abort()
    controller.abort()

    await expect(pendingWait).resolves.toBe(false)
    expect(vi.getTimerCount()).toBe(0)
    vi.useRealTimers()
  })
})

describe('live reduced motion reconciliation', () => {
  it.each([
    ['reel offset', 900],
    ['reel braking', 650],
  ])('snaps every reel to the authoritative result when enabled during %s', async (_, toggleAtMs) => {
    vi.useFakeTimers()
    installAnimationWindow()
    const harness = startCadenceHarness()
    await vi.advanceTimersByTimeAsync(toggleAtMs)

    harness.requestReducedMotion()
    await vi.advanceTimersByTimeAsync(50)
    await harness.completion

    expect(harness.displayedReels).toEqual(targetReels)
    expect(harness.reelMotion).toEqual(targetReels.map(() => 'stopped'))
    const winningPositions = [
      { reel: 0, row: 0 },
      { reel: 2, row: 2 },
      { reel: 4, row: 3 },
    ]
    for (const position of winningPositions) {
      expect(harness.displayedReels[position.reel][position.row])
        .toBe(targetReels[position.reel][position.row])
    }
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })
})

describe('reduced motion symbol policy', () => {
  it('always chooses static sources under reduced motion', () => {
    expect(shouldUseAnimatedSymbol(true, 'idle')).toBe(false)
    expect(shouldUseAnimatedSymbol(true, 'stopped')).toBe(false)
    expect(shouldUseAnimatedSymbol(true, 'accelerating')).toBe(false)
    expect(shouldUseAnimatedSymbol(true, 'spinning')).toBe(false)
    expect(shouldUseAnimatedSymbol(true, 'braking')).toBe(false)
  })

  it('only uses animated sources while an ordinary reel is idle or stopped', () => {
    expect(shouldUseAnimatedSymbol(false, 'idle')).toBe(true)
    expect(shouldUseAnimatedSymbol(false, 'stopped')).toBe(true)
    expect(shouldUseAnimatedSymbol(false, 'accelerating')).toBe(false)
    expect(shouldUseAnimatedSymbol(false, 'spinning')).toBe(false)
    expect(shouldUseAnimatedSymbol(false, 'braking')).toBe(false)
  })
})

describe('spin control state', () => {
  it('renders distinct visible Spin, Stop, and Stopping states', () => {
    const onSpin = () => undefined
    const idle = renderToStaticMarkup(createElement(SpinButton, { onSpin }))
    const spinning = renderToStaticMarkup(
      createElement(SpinButton, { isSpinning: true, onSpin }),
    )
    const stopping = renderToStaticMarkup(
      createElement(SpinButton, {
        isSpinning: true,
        isStopRequested: true,
        onSpin,
      }),
    )

    expect(idle).toContain('Spin</strong>')
    expect(idle).toContain('aria-label="Spin the reels"')
    expect(spinning).toContain('spin-button--active')
    expect(spinning).toContain('Stop</strong>')
    expect(spinning).toContain('aria-label="Stop the spin"')
    expect(stopping).toContain('spin-button--stopping')
    expect(stopping).toContain('Stopping</strong>')
  })
})

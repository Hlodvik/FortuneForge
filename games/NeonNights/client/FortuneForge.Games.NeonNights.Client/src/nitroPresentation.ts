import {
  prepareSpinForStopping, stopReelAnimation, waitForPresentation, waitForPresentationFrame, waitForReelStopOffset,
  type GridPosition, type SpinAnimation,
} from '@fortuneforge/slot-client-core'
import type { NeonNitroWild, NeonSpinResult, NeonSymbolId } from './types.js'

type NitroEvidence = NeonNitroWild & { secondNitro?: GridPosition | null }

export type NitroPresentation = {
  trigger: GridPosition
  secondNitro?: GridPosition
  targetReel: number
  phase: 'anticipating' | 'charging' | 'expanding'
}

const nitroAnticipationMs = 2_000
const nitroChargeMs = 720
export const nitroWildFlightMs = 560
const nitroWildStaggerMs = 90
const nitroWildSettleMs = 110

export function nitroWildFlightDelay(row: number) {
  return row * nitroWildStaggerMs
}

function chargePresentation(nitro: NitroEvidence, targetReel: number): NitroPresentation {
  const legacySecondRow = nitro.initialReels[nitro.eligibleReel]?.indexOf('NITRO') ?? -1
  const secondNitro = nitro.secondNitro ?? (legacySecondRow < 0 ? undefined : { reel: nitro.eligibleReel, row: legacySecondRow })
  return { trigger: nitro.trigger, secondNitro, targetReel, phase: 'charging' }
}

export async function presentNitroExpansion(nitro: NitroEvidence, signal: AbortSignal,
  onPresentationChange: (presentation: NitroPresentation | null) => void,
  showWildReel: (reelIndex: number) => void,
  showWildCell?: (reelIndex: number, rowIndex: number) => void) {
  const { expandedReel } = nitro
  if (expandedReel === null || signal.aborted) return

  const charging = chargePresentation(nitro, expandedReel)
  onPresentationChange(charging)
  try {
    if (!await waitForPresentation(nitroChargeMs, signal)) return
    onPresentationChange({ ...charging, phase: 'expanding' })
    if (!await waitForPresentationFrame(signal)) return

    if (showWildCell) {
      const arrivals = nitro.initialReels[expandedReel].map(async (_, row) => {
        if (await waitForPresentation(nitroWildFlightMs + nitroWildFlightDelay(row), signal)) showWildCell(expandedReel, row)
      })
      await Promise.all(arrivals)
    } else {
      const lastRow = nitro.initialReels[expandedReel].length - 1
      if (await waitForPresentation(nitroWildFlightMs + nitroWildFlightDelay(lastRow), signal)) showWildReel(expandedReel)
    }
    await waitForPresentation(nitroWildSettleMs, signal)
  } finally {
    onPresentationChange(null)
  }
}

export async function stopNitroReels({ animation, nitro, settledReels, signal, isQuickStopRequested,
  onReelStopped, onPresentationChange, onAnticipation, settleAfterLastReelMs }: {
  animation: SpinAnimation<NeonSymbolId>
  nitro: NitroEvidence
  settledReels: NeonSpinResult['reels']
  signal: AbortSignal
  isQuickStopRequested: () => boolean
  onReelStopped: () => void
  onPresentationChange: (presentation: NitroPresentation | null) => void
  onAnticipation: () => void
  settleAfterLastReelMs: number
}) {
  const { initialReels, trigger, eligibleReel, expandedReel } = nitro
  const canPresent = () => !animation.finished && !signal.aborted && !isQuickStopRequested()

  const stopColumns = (first: number, end: number) => Promise.all(initialReels.slice(first, end).map(async (targetSymbols, offset) => {
    const reelIndex = first + offset
    await waitForReelStopOffset(animation, isQuickStopRequested() ? reelIndex : offset, isQuickStopRequested)
    await stopReelAnimation(animation, {
      reelIndex, targetSymbols, isQuickStopRequested, skipMinimumDuration: true,
      quickBrakeDurationMs: 90, quickSettleAfterStopMs: 35,
      settleAfterStopMs: reelIndex === initialReels.length - 1 ? settleAfterLastReelMs : 0,
      onStopped: onReelStopped,
    })
    if (reelIndex === expandedReel && canPresent()) {
      const revealed: NeonSymbolId[] = [...initialReels[reelIndex]]
      await presentNitroExpansion(nitro, signal, onPresentationChange,
        index => animation.displayFrame(index, settledReels[index]),
        (index, row) => {
          revealed[row] = settledReels[index][row]
          animation.displayFrame(index, [...revealed])
        })
    }
  }))

  await prepareSpinForStopping(animation, isQuickStopRequested)
  await stopColumns(0, eligibleReel)
  // StopReelAnimation has painted the actual Nitro before the next reel receives the effect.
  if (canPresent()) {
    onPresentationChange({ trigger, targetReel: eligibleReel, phase: 'anticipating' })
    onAnticipation()
    await waitForPresentation(nitroAnticipationMs, signal)
  }
  await stopColumns(eligibleReel, eligibleReel + 1)
  if (expandedReel !== eligibleReel) {
    onPresentationChange(expandedReel !== null && canPresent() ? chargePresentation(nitro, expandedReel) : null)
  }
  await stopColumns(eligibleReel + 1, initialReels.length)
}

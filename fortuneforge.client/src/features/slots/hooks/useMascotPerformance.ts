import { useCallback, useEffect, useState } from 'react'
import type { MascotPhase, MascotSet } from '../../../components/WukongCompanion.config'

export type MascotOutcome = 'loss' | 'neutral' | 'win'

const NO_MASCOT_TIMING: MascotSet['timing'] = {
  successDurationMs: 0,
  returnDurationMs: 0,
  defeatDurationMs: 0,
  successTimeline: [0],
}

// Keeps mascot timing and animation frames independent from spin/account state.
export function useMascotPerformance(mascotSet: MascotSet | null) {
  const [actionKey, setActionKey] = useState(0)
  const [phase, setPhase] = useState<MascotPhase>('idle')
  const [successFrame, setSuccessFrame] = useState(0)
  const hasMascot = mascotSet !== null
  const timing = mascotSet?.timing ?? NO_MASCOT_TIMING

  useEffect(() => {
    if (!hasMascot) {
      setPhase('idle')
      setSuccessFrame(0)
      return undefined
    }

    const phaseDuration = phase === 'success-returning'
      ? timing.returnDurationMs
      : phase === 'celebrating'
        ? timing.successDurationMs
        : phase === 'returning'
          ? timing.returnDurationMs
          : phase === 'defeated'
            ? timing.defeatDurationMs
            : null

    if (phaseDuration === null) {
      return undefined
    }

    const phaseTimer = window.setTimeout(() => {
      setPhase(phase === 'success-returning' ? 'celebrating' : 'idle')
    }, phaseDuration)
    return () => window.clearTimeout(phaseTimer)
  }, [hasMascot, phase, timing])

  useEffect(() => {
    if (!hasMascot) {
      return undefined
    }

    if (phase !== 'celebrating') {
      return undefined
    }

    const frameCount = Math.max(1, timing.successTimeline.length)
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      setSuccessFrame(frameCount - 1)
      return undefined
    }

    const frameDurationMs = timing.successDurationMs / frameCount
    let startedAt: number | null = null
    let displayedFrame = -1
    let frameRequest = 0
    const advanceFrame = (timestamp: number) => {
      startedAt ??= timestamp
      const nextFrame = Math.min(
        frameCount - 1,
        Math.floor((timestamp - startedAt) / frameDurationMs),
      )
      if (nextFrame !== displayedFrame) {
        displayedFrame = nextFrame
        setSuccessFrame(nextFrame)
      }
      if (nextFrame < frameCount - 1) {
        frameRequest = window.requestAnimationFrame(advanceFrame)
      }
    }
    frameRequest = window.requestAnimationFrame(advanceFrame)
    return () => window.cancelAnimationFrame(frameRequest)
  }, [hasMascot, phase, timing])

  const beginPerformance = useCallback((animate: boolean) => {
    if (!hasMascot || !animate) {
      setPhase('idle')
      return
    }
    setActionKey((currentKey) => currentKey + 1)
    setSuccessFrame(0)
    setPhase('performing')
  }, [hasMascot])

  const completePerformance = useCallback((outcome: MascotOutcome, animate: boolean) => {
    if (!hasMascot || !animate) {
      setPhase('idle')
      return
    }
    setPhase(outcome === 'win' ? 'success-returning' : outcome === 'loss' ? 'defeated' : 'returning')
  }, [hasMascot])

  return {
    actionKey,
    beginPerformance,
    completePerformance,
    phase,
    successFrame,
  }
}

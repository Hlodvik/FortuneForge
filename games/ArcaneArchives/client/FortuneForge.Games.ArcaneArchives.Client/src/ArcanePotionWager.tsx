import { useLayoutEffect, useRef, useState } from 'react'
import { formatRand } from '@fortuneforge/slot-client-core'
import type { SlotPotionWagerReadoutProps } from '@fortuneforge/slot-game-runtime/experience'
import './potionWager.css'

export function ArcanePotionWager({
  wager, selectedWager, lockedPotionId, confirmedPotionUse, reducedMotion,
}: SlotPotionWagerReadoutProps) {
  const [roll, setRoll] = useState<{ bottleId: number; amount: number } | null>(null)
  const animationCompleted = useRef<number | null>(null)

  useLayoutEffect(() => {
    if (lockedPotionId === null || confirmedPotionUse !== lockedPotionId ||
        animationCompleted.current === lockedPotionId) return

    if (reducedMotion) {
      animationCompleted.current = lockedPotionId
      setRoll(null)
      return
    }

    const difference = wager - selectedWager
    const steps = Math.ceil(Math.abs(difference))
    const duration = Math.max(520, Math.min(1700, steps * 17))
    const direction = Math.sign(difference)
    let started: number | null = null
    let frame: number
    let lastStep = 0
    setRoll({ bottleId: lockedPotionId, amount: selectedWager })

    function count(time: number) {
      started ??= time
      const elapsed = time - started
      if (elapsed >= duration) {
        animationCompleted.current = lockedPotionId
        setRoll(null)
        return
      }

      const step = Math.floor(steps * elapsed / duration)
      if (step !== lastStep) {
        const amount = selectedWager + direction * Math.min(step, Math.abs(difference))
        if (lockedPotionId !== null) setRoll({ bottleId: lockedPotionId, amount })
        lastStep = step
      }
      frame = requestAnimationFrame(count)
    }

    frame = requestAnimationFrame(count)
    return () => cancelAnimationFrame(frame)
  }, [confirmedPotionUse, lockedPotionId, reducedMotion, selectedWager, wager])

  const locked = lockedPotionId !== null
  const running = roll !== null && roll.bottleId === lockedPotionId && !reducedMotion
  const spokenWager = formatRand(wager)
  return <span className="arcane-potion-wager" data-locked={locked || undefined}>
    <span className="slots-page__accessible-title" aria-live="polite">
      {locked ? `Potion wager locked at ${spokenWager}` : `Bet ${spokenWager}`}
    </span>
    <span aria-hidden="true">{locked ? 'Potion wager' : 'Bet'}</span>
    <strong className="arcane-potion-wager__amount" aria-hidden="true" data-rolling={running || undefined}>
      {formatRand(running ? roll.amount : wager)}
    </strong>
  </span>
}

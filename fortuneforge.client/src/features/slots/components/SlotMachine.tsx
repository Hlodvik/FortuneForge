import type { ReactNode } from 'react'
import { ReelWindow } from './ReelWindow'
import { SlotGameFrame } from './SlotGameFrame'
import { SpinLever } from './SpinLever'
import '../styles/slots.css'

type SlotMachineProps = {
  reelCount: number
  renderReel?: (index: number) => ReactNode
  disabled?: boolean
  isSpinning?: boolean
  onSpin: () => void
}

export function SlotMachine({
  reelCount,
  renderReel,
  disabled,
  isSpinning,
  onSpin,
}: SlotMachineProps) {
  return (
    <SlotGameFrame>
      <div className="slot-machine">
        <ReelWindow reelCount={reelCount} renderReel={renderReel} />
        <SpinLever disabled={disabled} isSpinning={isSpinning} onSpin={onSpin} />
      </div>
    </SlotGameFrame>
  )
}

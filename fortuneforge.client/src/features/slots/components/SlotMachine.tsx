import type { ReactNode } from 'react'
import type { SlotCabinetTheme } from '../config/cabinetThemes'
import { Reel } from './Reel'
import { SlotGameFrame } from './SlotGameFrame'
import '../styles/index.css'

type SlotMachineProps = {
  cabinetTheme: SlotCabinetTheme
  reelCount: number
  renderReel?: (index: number) => ReactNode
}

export function SlotMachine({
  cabinetTheme,
  reelCount,
  renderReel,
}: SlotMachineProps) {
  const safeReelCount = Math.max(1, Math.floor(reelCount))

  return (
    <SlotGameFrame cabinetTheme={cabinetTheme}>
      <div className="slot-machine">
        <div className="slot-machine__playfield">
          <div
            className="slot-machine__reels"
            style={{ '--reel-count': safeReelCount } as React.CSSProperties}
            aria-label={`${safeReelCount}-reel slot container`}
          >
            {Array.from({ length: safeReelCount }, (_, index) => (
              <Reel index={index} key={index}>
                {renderReel?.(index)}
              </Reel>
            ))}
          </div>
        </div>
      </div>
    </SlotGameFrame>
  )
}

import type { ReactNode } from 'react'
import { Reel } from './Reel'

type ReelWindowProps = {
  reelCount: number
  renderReel?: (index: number) => ReactNode
}

export function ReelWindow({ reelCount, renderReel }: ReelWindowProps) {
  const safeReelCount = Math.max(1, Math.floor(reelCount))

  return (
    <div
      className="reel-window"
      style={{ '--reel-count': safeReelCount } as React.CSSProperties}
      aria-label={`${safeReelCount}-reel slot window`}
    >
      <div className="reel-window__reels">
        {Array.from({ length: safeReelCount }, (_, index) => (
          <Reel index={index} key={index}>
            {renderReel?.(index)}
          </Reel>
        ))}
      </div>
      <span className="reel-window__shine" aria-hidden="true" />
    </div>
  )
}

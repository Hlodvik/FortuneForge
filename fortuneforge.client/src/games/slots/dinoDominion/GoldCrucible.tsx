import type { CSSProperties } from 'react'
import crucibleStages from '../../../assets/slots/games/gold-foundry/optimized/gold-foundry-crucible-stages-v1.webp'
import type { SlotEnergyArtworkProps } from '../../../features/slots/config/slotFeatures'
import './goldCrucible.css'

export const GOLD_CRUCIBLE_POUR_MS = 1050

const fillFrameCount = 5

function framePosition(index: number): string {
  return `${(index % 3) * 50}% ${Math.floor(index / 3) * 100}%`
}

export function GoldCrucible({ progress, completing: pouring }: SlotEnergyArtworkProps) {
  const fill = pouring ? 1 : Math.min(1, Math.max(0, progress))
  const stage = fill * (fillFrameCount - 1)
  const lowerStage = Math.floor(stage)
  const upperStage = Math.min(fillFrameCount - 1, lowerStage + 1)

  return (
    <div
      aria-hidden="true"
      className="gold-crucible"
      data-pouring={pouring}
      style={{
        '--crucible-art': `url("${crucibleStages}")`,
        '--crucible-pour-duration': `${GOLD_CRUCIBLE_POUR_MS}ms`,
      } as CSSProperties}
    >
      <div className="gold-crucible__fill">
        <div className="gold-crucible__frame" style={{ backgroundPosition: framePosition(lowerStage) }} />
        <div
          className="gold-crucible__frame"
          style={{ backgroundPosition: framePosition(upperStage), opacity: stage - lowerStage }}
        />
      </div>
      <div className="gold-crucible__frame gold-crucible__pour" style={{ backgroundPosition: framePosition(fillFrameCount) }} />
      <div className="gold-crucible__frame gold-crucible__reset" style={{ backgroundPosition: framePosition(0) }} />
    </div>
  )
}

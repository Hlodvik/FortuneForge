import type { CSSProperties } from 'react'
import crucibleStages from '../../../assets/slots/games/gold-foundry/optimized/gold-foundry-crucible-stages-v1.webp'
import type { SlotEnergyAwardMeterProps } from '../../../features/slots/config/slotFeatures'
import './goldCrucible.css'

export const GOLD_CRUCIBLE_POUR_MS = 1050

const fillFrameCount = 5
const money = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const formatCents = (cents: number) => `R${money.format(cents / 100)}`

function framePosition(index: number): string {
  return `${(index % 3) * 50}% ${Math.floor(index / 3) * 100}%`
}

function GoldCrucible({ progress, pouring }: { progress: number; pouring: boolean }) {
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

export function GoldCrucibleMeter({ balance, capacity, meterRef, award, completing }: SlotEnergyAwardMeterProps) {
  const progress = Math.min(1, balance / capacity)
  const payoutCents = award?.payoutCents ?? 0
  const value = formatCents(completing ? payoutCents : award?.valueCents ?? 0)

  return (
    <div className="slots-page__crucible">
      <div
        className="slots-page__crucible-art"
        ref={meterRef}
        role="progressbar"
        aria-label="Gold crucible"
        aria-valuemin={0}
        aria-valuemax={capacity}
        aria-valuenow={Math.min(capacity, balance)}
        aria-valuetext={`${Math.floor(progress * 100)}% full. Pour value ${value}`}
        data-pouring={completing ? 'true' : undefined}
      >
        <GoldCrucible progress={progress} pouring={completing} />
      </div>
      <span className="slots-page__crucible-copy">
        <span>{completing ? 'Pouring' : 'Pour value'}</span>
        <strong>{value}</strong>
        <small className="slots-page__crucible-receipt" role="status">
          {!completing && payoutCents > 0 ? `${formatCents(payoutCents)} credited` : null}
        </small>
      </span>
    </div>
  )
}

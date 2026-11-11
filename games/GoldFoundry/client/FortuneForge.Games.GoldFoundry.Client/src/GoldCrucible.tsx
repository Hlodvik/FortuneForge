import type { CSSProperties, RefObject } from 'react'
import type { GoldCrucibleAward } from './crucibleAward.js'
import { GOLD_FOUNDRY_VISUALS } from './visuals.js'
import './goldCrucible.css'

export const GOLD_CRUCIBLE_POUR_MS = 1050
const fillFrameCount = 5
const formatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const formatCents = (value: number) => `R${formatter.format(value / 100)}`
const framePosition = (index: number) => `${(index % 3) * 50}% ${Math.floor(index / 3) * 100}%`

function GoldCrucible({ progress, pouring }: { progress: number; pouring: boolean }) {
  const fill = pouring ? 1 : Math.min(1, Math.max(0, progress))
  const stage = fill * (fillFrameCount - 1)
  const lower = Math.floor(stage)
  const upper = Math.min(fillFrameCount - 1, lower + 1)
  return <div aria-hidden="true" className="gold-crucible" data-pouring={pouring} style={{
    '--crucible-art': `url("${GOLD_FOUNDRY_VISUALS.crucibleStages}")`,
    '--crucible-pour-duration': `${GOLD_CRUCIBLE_POUR_MS}ms`,
  } as CSSProperties}>
    <div className="gold-crucible__fill">
      <div className="gold-crucible__frame" style={{ backgroundPosition: framePosition(lower) }} />
      <div className="gold-crucible__frame" style={{ backgroundPosition: framePosition(upper), opacity: stage - lower }} />
    </div>
    <div className="gold-crucible__frame gold-crucible__pour" style={{ backgroundPosition: framePosition(fillFrameCount) }} />
    <div className="gold-crucible__frame gold-crucible__reset" style={{ backgroundPosition: framePosition(0) }} />
  </div>
}

export function GoldCrucibleMeter({ balance, capacity, meterRef, award, completing }: {
  balance: number
  capacity: number
  meterRef: RefObject<HTMLDivElement | null>
  award: GoldCrucibleAward | null
  completing: boolean
}) {
  const progress = Math.min(1, balance / capacity)
  const payoutCents = award?.payoutCents ?? 0
  const value = formatCents(completing ? payoutCents : award?.valueCents ?? 0)
  return <div className="slots-page__crucible">
    <div className="slots-page__crucible-art" ref={meterRef} role="progressbar" aria-label="Gold crucible"
      aria-valuemin={0} aria-valuemax={capacity} aria-valuenow={Math.min(capacity, balance)}
      aria-valuetext={`${Math.floor(progress * 100)}% full. Pour value ${value}`}
      data-pouring={completing ? 'true' : undefined}>
      <GoldCrucible progress={progress} pouring={completing} />
    </div>
    <span className="slots-page__crucible-copy">
      <span>{completing ? 'Pouring' : 'Pour value'}</span><strong>{value}</strong>
      <small className="slots-page__crucible-receipt" role="status">
        {!completing && payoutCents > 0 ? `${formatCents(payoutCents)} credited` : null}
      </small>
    </span>
  </div>
}

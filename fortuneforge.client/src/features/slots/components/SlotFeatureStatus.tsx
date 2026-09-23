import type {
  SlotCollectionFeature,
  SlotHelpDefinition,
  SlotSpecialRoundFeature,
} from '../config/slotFeatures'
import type { SlotSealCollection } from '../types/slots'
import { getSlotFeatureProgress } from '../presentation/slotFeatureProgress'

type SlotFeatureStatusProps = {
  collections?: SlotCollectionFeature
  collectionStates: readonly SlotSealCollection[]
  freeSpinsRemaining: number
  help: SlotHelpDefinition
  isActive: boolean
  label: string
  specialRound: SlotSpecialRoundFeature
}

export function SlotFeatureStatus({
  collections,
  collectionStates,
  freeSpinsRemaining,
  help,
  isActive,
  label,
  specialRound,
}: SlotFeatureStatusProps) {
  const progress = getSlotFeatureProgress(collections, collectionStates)
  const scatterRequirement = help.freeGames?.requiredSymbols
  const scatterLabel = help.freeGames?.symbolLabel ?? 'FREE GAME symbols'

  return (
    <aside
      className={`slots-page__special-round${isActive ? ' slots-page__special-round--active' : ''}`}
      data-feature-state={isActive ? 'active' : progress ? 'collecting' : 'seeking'}
      aria-live="polite"
    >
      <div className="slots-page__special-round-heading">
        <span>{isActive ? 'Feature active' : 'Feature path'}</span>
        <strong>{isActive ? label : specialRound.title}</strong>
      </div>

      {isActive ? (
        <div className="slots-page__special-round-count" aria-label={`${freeSpinsRemaining} feature spins remaining`}>
          <strong>{freeSpinsRemaining}</strong>
          <span>spins left</span>
        </div>
      ) : progress ? (
        <div className="slots-page__special-round-progress">
          <span>
            Closest track: <strong>{progress.label}</strong>
            <b>{progress.current}/{progress.target}</b>
          </span>
          <span
            className="slots-page__special-round-track"
            role="progressbar"
            aria-label={`${progress.label}: ${progress.current} of ${progress.target}`}
            aria-valuemin={0}
            aria-valuemax={progress.target}
            aria-valuenow={progress.current}
          >
            <span style={{ width: `${progress.percent}%` }} />
          </span>
        </div>
      ) : scatterRequirement ? (
        <div className="slots-page__special-round-count" aria-label={`${scatterRequirement} ${scatterLabel} required`}>
          <strong>{scatterRequirement}</strong>
          <span>{scatterLabel}</span>
        </div>
      ) : null}

      <p>
        {isActive
          ? `${label} is running. Press Spin to skip the pause before the next feature spin.`
          : progress?.rewardDescription ?? specialRound.earnHint ?? specialRound.earnLabel}
      </p>

      {!isActive && (
        <div className="slots-page__special-round-steps" aria-label="Feature instructions">
          <span><b>Paylines</b>{help.paylineCount} routes begin on reel 1</span>
          {scatterRequirement && <span><b>Trigger</b>{scatterRequirement} {scatterLabel} in one spin</span>}
          {progress && <span><b>Build</b>{progress.label} is {progress.percent}% complete</span>}
          <span><b>Reward</b>{help.freeGames?.awardedSpins ?? 'Bonus'} {help.freeGames?.awardLabel ?? 'feature spins'}</span>
        </div>
      )}
    </aside>
  )
}

import type { CSSProperties } from 'react'
import type {
  SlotCollectionDefinition,
  SlotCollectionPresentation,
} from '../config/slotFeatures'
import type { SlotSealCollection } from '../types/slots'

type CollectionProgressDisplayProps = {
  collection: SlotSealCollection
  definition: SlotCollectionDefinition
  image: string
  isImpacting: boolean
  itemLabel: string
  containerImage?: string
  displayCount?: number
  isCelebrating?: boolean
  presentation: SlotCollectionPresentation
  showCount?: boolean
  statusDetail?: string
}

const pileLayout = [
  { x: -1.8, y: -0.02, rotate: -19, scale: 0.72 },
  { x: 1.68, y: -0.08, rotate: 17, scale: 0.73 },
  { x: -0.88, y: -0.42, rotate: -9, scale: 0.78 },
  { x: 0.82, y: -0.48, rotate: 11, scale: 0.79 },
  { x: -2.1, y: -0.82, rotate: -24, scale: 0.68 },
  { x: 2, y: -0.86, rotate: 22, scale: 0.69 },
  { x: -1.18, y: -1.04, rotate: -12, scale: 0.74 },
  { x: 1.15, y: -1.08, rotate: 12, scale: 0.75 },
  { x: -0.46, y: -1.4, rotate: -5, scale: 0.8 },
  { x: 0.44, y: -1.42, rotate: 6, scale: 0.81 },
] as const

const piratePileLayout = [
  { x: 0, y: 0, rotate: -5, scale: 0.85 },
  { x: -1.12, y: -0.02, rotate: -20, scale: 0.73 },
  { x: 1.1, y: -0.04, rotate: 18, scale: 0.74 },
  { x: -0.62, y: -0.48, rotate: -8, scale: 0.8 },
  { x: 0.58, y: -0.51, rotate: 9, scale: 0.81 },
  { x: -1.48, y: -0.65, rotate: -25, scale: 0.68 },
  { x: 1.46, y: -0.69, rotate: 23, scale: 0.69 },
  { x: -0.94, y: -1.04, rotate: -14, scale: 0.76 },
  { x: 0.89, y: -1.07, rotate: 13, scale: 0.76 },
  { x: 0, y: -1.34, rotate: 2, scale: 0.83 },
] as const

const orbitSteps = Array.from({ length: 10 }, (_, index) => index)

export function CollectionProgressDisplay({
  collection,
  definition,
  image,
  isImpacting,
  itemLabel,
  displayCount,
  isCelebrating = false,
  presentation,
  showCount = true,
  statusDetail,
}: CollectionProgressDisplayProps) {
  const visualCount = displayCount ?? collection.count
  const isGemHoard = presentation === 'gem-hoard'
  const progress = isGemHoard
    ? displayCount !== undefined || isCelebrating
      ? 100
      : Number((94 * (1 - Math.exp(-Math.max(0, visualCount) / 8))).toFixed(1))
    : Math.min(100, visualCount / collection.requiredCount * 100)
  const visiblePieceCount = isGemHoard
    ? Math.min(piratePileLayout.length, Math.ceil(Math.max(0, visualCount) / 2.4))
    : visualCount === 0 ? 0 : Math.max(1, Math.ceil(progress / 10))
  const collectionName = isGemHoard ? `${definition.shortLabel} gems` : definition.label
  const collectible = isGemHoard ? `${definition.shortLabel} gems` : itemLabel
  const rewardDescription = definition.rewardDescription ?? 'Fill it to unlock a special round.'
  const collectionHint = isGemHoard
    ? `${collectionName}: land matching gems anywhere on the reels to grow the pile and build the chance of a feature. ${rewardDescription}`
    : `${collectionName}: land ${collectible} anywhere on the reels to complete it. ${collection.count} of ${collection.requiredCount} collected; ${rewardDescription}`
  const tooltipId = `slot-collection-hint-${collection.sealId}`

  return (
    <div
      className={`slots-page__seal-collection slots-page__seal-collection--${collection.sealId} slots-page__seal-collection--${presentation}${isImpacting ? ' slots-page__seal-collection--impact' : ''}${isCelebrating ? ' slots-page__seal-collection--celebrating' : ''}`}
      data-seal-id={collection.sealId}
      data-collection-presentation={presentation}
      role={isGemHoard ? 'img' : 'progressbar'}
      aria-label={`${definition.label}: ${collectionHint}`}
      aria-describedby={tooltipId}
      aria-valuemin={isGemHoard ? undefined : 0}
      aria-valuemax={isGemHoard ? undefined : collection.requiredCount}
      aria-valuenow={isGemHoard ? undefined : collection.count}
      tabIndex={0}
      style={{ '--collection-progress': `${progress}%` } as CSSProperties}
    >
      <span className="slots-page__collection-visual" aria-hidden="true">
        {presentation === 'celestial-orbit' ? (
          <span className="slots-page__collection-orbit">
            <span className="slots-page__collection-orbit-track">
              {orbitSteps.map((index) => (
                <i
                  className={index < visiblePieceCount ? 'is-lit' : ''}
                  key={index}
                  style={{ '--collection-orbit-index': index } as CSSProperties}
                />
              ))}
            </span>
            <span className="slots-page__collection-orbit-core">
              <img src={image} alt="" />
            </span>
          </span>
        ) : presentation === 'juice-glass' ? (
          <>
            <span className="slots-page__juice-glass">
              <span
                className="slots-page__juice-fill"
                style={{ height: `${progress}%` }}
              >
                <i />
                <i />
                <i />
              </span>
            </span>
            <img className="slots-page__collection-garnish" src={image} alt="" />
          </>
        ) : isGemHoard ? (
          <span className="slots-page__pirate-gem-pile">
            {piratePileLayout.map((layout, index) => (
              <img
                className={index < visiblePieceCount ? 'is-visible' : ''}
                key={index}
                src={image}
                alt=""
                draggable={false}
                style={{
                  '--collection-piece-x': `${layout.x * 16}%`,
                  '--collection-piece-y': `${layout.y * 24}%`,
                  '--collection-piece-rotate': `${layout.rotate}deg`,
                  '--collection-piece-scale': layout.scale,
                } as CSSProperties}
              />
            ))}
          </span>
        ) : (
          <>
            <span className="slots-page__collection-pile">
              {pileLayout.map((layout, index) => (
                <img
                  className="slots-page__collection-piece"
                  key={index}
                  src={image}
                  alt=""
                  style={{
                    '--collection-piece-x': `${layout.x}rem`,
                    '--collection-piece-y': `${layout.y}rem`,
                    '--collection-piece-rotate': `${layout.rotate}deg`,
                    '--collection-piece-scale': layout.scale,
                    '--collection-piece-visible': index < visiblePieceCount ? 1 : 0,
                  } as CSSProperties}
                />
              ))}
            </span>
            <img className="slots-page__collection-emblem" src={image} alt="" />
          </>
        )}
      </span>

      <span className="slots-page__seal-details">
        <strong className="slots-page__seal-title" aria-hidden={definition.displayLabel ? true : undefined}>
          {definition.displayLabel ?? definition.label}
        </strong>
        {isGemHoard && (
          <span className="slots-page__pirate-gem-meter" aria-hidden="true"><span /></span>
        )}
        {showCount && !isGemHoard && (
          <span className="slots-page__collection-count" aria-hidden="true">
            <b>{collection.count}</b>
            <small>/{collection.requiredCount}</small>
          </span>
        )}
        {statusDetail && (
          <span className="slots-page__collection-status-detail" aria-live="polite">
            {statusDetail}
          </span>
        )}
      </span>

      <span className="slots-page__collection-tooltip" id={tooltipId} role="tooltip">
        <span className="slots-page__collection-tooltip-kicker">Collection reward</span>
        <strong>{collectionName}</strong>
        <span className="slots-page__collection-tooltip-copy">Land {collectible} anywhere on the reels to bank them.</span>
        <span className="slots-page__collection-tooltip-progress">
          {isGemHoard ? rewardDescription : <><b>{collection.count}/{collection.requiredCount}</b> collected · {rewardDescription}</>}
        </span>
      </span>
    </div>
  )
}

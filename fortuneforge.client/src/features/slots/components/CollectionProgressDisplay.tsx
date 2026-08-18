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
  presentation: SlotCollectionPresentation
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

const orbitSteps = Array.from({ length: 10 }, (_, index) => index)

export function CollectionProgressDisplay({
  collection,
  definition,
  image,
  isImpacting,
  itemLabel,
  presentation,
}: CollectionProgressDisplayProps) {
  const progress = Math.min(100, collection.count / collection.requiredCount * 100)
  const visiblePieceCount = collection.count === 0
    ? 0
    : Math.max(1, Math.ceil(progress / 10))

  return (
    <div
      className={`slots-page__seal-collection slots-page__seal-collection--${collection.sealId} slots-page__seal-collection--${presentation}${isImpacting ? ' slots-page__seal-collection--impact' : ''}`}
      data-seal-id={collection.sealId}
      data-collection-presentation={presentation}
      role="progressbar"
      aria-label={`${definition.label}: ${collection.count} of ${collection.requiredCount} ${itemLabel}`}
      aria-valuemin={0}
      aria-valuemax={collection.requiredCount}
      aria-valuenow={collection.count}
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
        <strong className="slots-page__seal-title">{definition.label}</strong>
        <span className="slots-page__collection-count" aria-hidden="true">
          <b>{collection.count}</b>
          <small>/{collection.requiredCount}</small>
        </span>
      </span>
    </div>
  )
}

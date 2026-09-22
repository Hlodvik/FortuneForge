import type { CSSProperties } from 'react'

type TreasureGemFlyoverState = {
  id: number
  collectionId: string
  left: number
  top: number
  width: number
  height: number
  travelX: number
  travelY: number
  durationMs: number
  chestDropHeight: number
}

type TreasureGemFlyoverProps = {
  flyover: TreasureGemFlyoverState
  image: string
}

const gemCascade = [
  { startX: -16, startY: 5, endX: -18, endY: 3, rotate: -24 },
  { startX: 12, startY: -8, endX: 15, endY: -5, rotate: 18 },
  { startX: -5, startY: -16, endX: -8, endY: 6, rotate: -11 },
  { startX: 18, startY: 5, endX: 20, endY: 1, rotate: 29 },
  { startX: -21, startY: -4, endX: -24, endY: -3, rotate: -31 },
  { startX: 3, startY: 12, endX: 2, endY: 7, rotate: 8 },
  { startX: -10, startY: 11, endX: -12, endY: 0, rotate: -16 },
] as const

export function TreasureGemFlyover({ flyover, image }: TreasureGemFlyoverProps) {
  const gemFlightDuration = Math.max(240, Math.round(flyover.durationMs * 0.65))
  const gemStaggerMs = (flyover.durationMs - gemFlightDuration) /
    Math.max(1, gemCascade.length - 1)
  const gemSize = Math.max(14, flyover.width * 0.46)

  return gemCascade.map((piece, index) => (
    <img
      key={`${flyover.id}-${index}`}
      className={`slots-page__seal-flyover slots-page__seal-flyover--${flyover.collectionId} slots-page__seal-flyover--gem-hoard`}
      data-seal-id={flyover.collectionId}
      src={image}
      alt=""
      aria-hidden="true"
      style={{
        left: flyover.left + (flyover.width - gemSize) / 2,
        top: flyover.top + (flyover.height - gemSize) / 2,
        width: gemSize,
        height: gemSize,
        animationDuration: `${gemFlightDuration}ms`,
        animationDelay: `${gemStaggerMs * index}ms`,
        '--seal-travel-x': `${flyover.travelX}px`,
        '--seal-travel-y': `${flyover.travelY}px`,
        '--treasure-gem-drop-height': `${flyover.chestDropHeight}px`,
        '--treasure-gem-start-x': `${piece.startX}px`,
        '--treasure-gem-start-y': `${piece.startY}px`,
        '--treasure-gem-end-x': `${piece.endX}px`,
        '--treasure-gem-end-y': `${piece.endY}px`,
        '--treasure-gem-rotate': `${piece.rotate}deg`,
      } as CSSProperties}
    />
  ))
}

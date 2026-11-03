import { useLayoutEffect, useRef, useState } from 'react'
import type { GridPosition, LightningCollection } from '../../../features/slots/types/slots'
import './lightningCollection.css'

type Point = { x: number; y: number }

type LightningGeometry = {
  width: number
  height: number
  bolts: { key: string; points: string }[]
}

function boltPoints(origin: Point, target: Point): string {
  const dx = target.x - origin.x
  const dy = target.y - origin.y
  const length = Math.hypot(dx, dy)
  const bend = Math.min(12, length * 0.14)
  const offsets = [0, 1, -0.7, 0.55, 0]
  return offsets.map((offset, index) => {
    const progress = index / (offsets.length - 1)
    const perpendicular = length === 0 ? 0 : offset * bend / length
    return `${origin.x + dx * progress - dy * perpendicular},${origin.y + dy * progress + dx * perpendicular}`
  }).join(' ')
}

export function ArcaneLightningCollection({ collection, reducedMotion }: {
  collection: LightningCollection
  reducedMotion: boolean
}) {
  const overlay = useRef<SVGSVGElement>(null)
  const [geometry, setGeometry] = useState<LightningGeometry | null>(null)

  useLayoutEffect(() => {
    const svg = overlay.current
    const reels = svg?.parentElement
    if (!svg || !reels) return

    const measure = () => {
      const bounds = svg.getBoundingClientRect()
      const center = ({ reel, row }: GridPosition): Point | undefined => {
        const rect = reels.querySelector(
          `.slot-symbol[data-reel-index="${reel}"][data-row-index="${row}"]`,
        )?.getBoundingClientRect()
        if (!rect || rect.width <= 0 || rect.height <= 0) return undefined
        return { x: rect.left + rect.width / 2 - bounds.left, y: rect.top + rect.height / 2 - bounds.top }
      }
      const origin = center(collection.collector)
      if (!origin || bounds.width <= 0 || bounds.height <= 0) {
        setGeometry(null)
        return
      }
      const bolts = collection.cells.flatMap(position => {
        const target = center(position)
        return target ? [{ key: `${position.reel}:${position.row}`, points: boltPoints(origin, target) }] : []
      })
      setGeometry({ width: bounds.width, height: bounds.height, bolts })
    }

    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(reels)
    window.addEventListener('resize', measure)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [collection])

  return <svg
    ref={overlay}
    className="arcane-lightning-collection"
    data-reduced-motion={reducedMotion ? 'true' : undefined}
    aria-hidden="true"
    focusable="false"
    viewBox={`0 0 ${geometry?.width ?? 1} ${geometry?.height ?? 1}`}
    preserveAspectRatio="none"
  >
    {geometry?.bolts.map(({ key, points }) => <polyline
      key={key}
      points={points}
      pathLength="1"
      vectorEffect="non-scaling-stroke"
    />)}
  </svg>
}

import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { GridPosition, PaylinePayout } from '../../../features/slots/types/slots'
import { getMidnightPaidPaths, type MidnightPaidPath } from './midnightPaidPaths'
import './midnightWinningPaylines.css'

type MeasuredPath = MidnightPaidPath & Readonly<{ points: string; labelX: number; labelY: number }>
type Geometry = Readonly<{ resultKey: number | string; width: number; height: number; paths: readonly MeasuredPath[] }>
export type ActivePaidPath = Readonly<{ resultKey: number | string; positions: readonly GridPosition[] }>

function pathMidpoint(points: readonly (readonly [number, number])[]): readonly [number, number] {
  const lengths = points.slice(1).map((point, index) => Math.hypot(point[0] - points[index][0], point[1] - points[index][1]))
  let remaining = lengths.reduce((total, length) => total + length, 0) / 2
  for (let index = 0; index < lengths.length; index++) {
    const length = lengths[index]
    if (remaining <= length) {
      const fraction = length === 0 ? 0 : remaining / length
      return [points[index][0] + (points[index + 1][0] - points[index][0]) * fraction,
        points[index][1] + (points[index + 1][1] - points[index][1]) * fraction]
    }
    remaining -= length
  }
  return points[0]
}

type MidnightWinningPaylinesProps = Readonly<{
  paylines: readonly PaylinePayout[]
  resultKey: number | string
  visible: boolean
  reducedMotion: boolean
  palette?: 'gold' | 'neon' | 'rainbow' | 'cabinet'
  formatWinAmount?: (amountPoints: number) => string
  onActivePathChange?: (path: ActivePaidPath | null) => void
}>

/** Render beside SlotMachine inside the existing, positioned cabinet spotlight. */
export function MidnightWinningPaylines({
  paylines, resultKey, visible, reducedMotion, palette = 'gold', formatWinAmount, onActivePathChange,
}: MidnightWinningPaylinesProps) {
  const isRainbow = palette === 'rainbow'
  const overlay = useRef<SVGSVGElement>(null)
  const paidPaths = useMemo(() => getMidnightPaidPaths(paylines), [paylines])
  const [geometry, setGeometry] = useState<Geometry | null>(null)
  const [cycle, setCycle] = useState({ resultKey, index: 0 })

  useLayoutEffect(() => {
    if (!visible || paidPaths.length === 0) return
    const svg = overlay.current
    const spotlight = svg?.closest('.slots-page__cabinet-spotlight')
    const reels = spotlight?.querySelector('.slot-machine__reels')
    if (!svg || !spotlight || !reels) return
    const measure = () => {
      const bounds = svg.getBoundingClientRect()
      if (bounds.width <= 0 || bounds.height <= 0) { setGeometry(null); return }
      const centers = new Map<string, readonly [number, number]>()
      for (const cell of reels.querySelectorAll<HTMLElement>('.slot-symbol[data-reel-index][data-row-index]')) {
        const rect = cell.getBoundingClientRect()
        if (rect.width <= 0 || rect.height <= 0) continue
        const x = rect.left + rect.width / 2 - bounds.left
        const y = rect.top + rect.height / 2 - bounds.top
        if (x < 0 || y < 0 || x > bounds.width || y > bounds.height) continue
        centers.set(`${cell.dataset.reelIndex}:${cell.dataset.rowIndex}`, [Number(x.toFixed(2)), Number(y.toFixed(2))])
      }
      const paths = paidPaths.flatMap(path => {
        const points = path.positions.map(position => centers.get(`${position.reel}:${position.row}`))
        if (!points.every((point): point is readonly [number, number] => point !== undefined)) return []
        const [labelX, labelY] = pathMidpoint(points)
        return [{ ...path,
          points: points.map(point => point.join(',')).join(' '),
          labelX: isRainbow ? labelX : Math.max(64, Math.min(bounds.width - 64, labelX)),
          labelY: isRainbow ? labelY : Math.max(26, labelY - 16) }]
      })
      const next = { resultKey, width: bounds.width, height: bounds.height, paths }
      setGeometry(previous => JSON.stringify(previous) === JSON.stringify(next) ? previous : next)
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(spotlight)
    observer?.observe(reels)
    window.addEventListener('resize', measure)
    return () => { observer?.disconnect(); window.removeEventListener('resize', measure) }
  }, [isRainbow, paidPaths, resultKey, visible])

  const currentGeometry = geometry?.resultKey === resultKey ? geometry : null
  const pathCount = currentGeometry?.paths.length ?? 0
  useEffect(() => {
    if (!visible || reducedMotion || pathCount < 2) return
    const timer = window.setInterval(() => setCycle(current => ({ resultKey,
      index: ((current.resultKey === resultKey ? current.index : 0) + 1) % pathCount,
    })), 2200)
    return () => window.clearInterval(timer)
  }, [pathCount, reducedMotion, resultKey, visible])

  const showPayline = visible && paidPaths.length > 0
  const index = reducedMotion || cycle.resultKey !== resultKey ? 0 : cycle.index % Math.max(1, pathCount)
  const path = showPayline ? currentGeometry?.paths[index] : undefined
  useLayoutEffect(() => {
    onActivePathChange?.(path ? { resultKey, positions: path.positions } : null)
  }, [onActivePathChange, path, resultKey])

  if (!showPayline) return null
  return <svg ref={overlay} className="midnight-winning-paylines" aria-hidden="true" focusable="false"
    data-palette={palette}
    data-reduced-motion={reducedMotion ? 'true' : undefined}
    viewBox={`0 0 ${currentGeometry?.width ?? 1} ${currentGeometry?.height ?? 1}`} preserveAspectRatio="none">
    {path && <g key={`${resultKey}:${path.id}`} data-payline-id={path.paylineId} data-points={path.points}>
      {isRainbow && <polyline className="midnight-winning-paylines__trail rainbow-realm__steady-payline"
        points={path.points} vectorEffect="non-scaling-stroke" strokeWidth={3.5} />}
      {palette === 'cabinet' && reducedMotion && <polyline className="midnight-winning-paylines__trail"
        points={path.points} vectorEffect="non-scaling-stroke" strokeWidth={3} strokeOpacity={0.85} />}
      <g className="midnight-winning-paylines__glint">
        {[[24, 9, .07], [19, 6, .12], [14, 4, .23], [9, 2.8, .44], [4, 1.6, .85]].map(([length, width, opacity], index) =>
          <polyline key={length} className={`midnight-winning-paylines__trail${index < 2 ? ' midnight-winning-paylines__trail--soft' : ''}`}
            points={path.points} pathLength="100" vectorEffect="non-scaling-stroke"
            strokeWidth={width} strokeOpacity={opacity} strokeDasharray={`${length} 200`} strokeDashoffset={length}>
            {!reducedMotion && <animate attributeName="stroke-dashoffset" from={length} to={length - 100}
              dur="2.2s" repeatCount="indefinite" />}
          </polyline>,
        )}
        <g>
          <ellipse className="midnight-winning-paylines__bloom" cx="0" cy="0" rx="7" ry="3" />
          <ellipse className="midnight-winning-paylines__core" cx="0" cy="0" rx="2.8" ry="1.4" />
          {!reducedMotion && <animateMotion dur="2.2s" repeatCount="indefinite" rotate="auto"
            path={`M${path.points.split(' ').join(' L')}`} />}
        </g>
      </g>
      {formatWinAmount && <text className="midnight-winning-paylines__amount" x={path.labelX} y={path.labelY}
        textAnchor="middle" dominantBaseline={isRainbow ? 'central' : undefined}>{formatWinAmount(path.amountPoints)}</text>}
    </g>}
  </svg>
}

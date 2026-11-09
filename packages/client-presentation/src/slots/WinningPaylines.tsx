import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { getPaidPaths, type PaidPath, type PaylinePosition, type PresentedPayline } from './paidPaths.js'
import './winningPaylines.css'

export { getPaidPaths } from './paidPaths.js'
export type { PaidPath, PaylinePosition, PresentedPayline } from './paidPaths.js'
export type ActivePaidPath = Readonly<{ resultKey: number | string; positions: readonly PaylinePosition[] }>
export type PaylineAppearance = Readonly<{
  classPrefix?: string
  variant?: string
  steadyTrail?: boolean
  steadyTrailClassName?: string
  reducedMotionTrail?: boolean
  labelInset?: number
  labelOffset?: number
  labelMinimumY?: number
  labelBaseline?: 'central'
  amountGradient?: readonly Readonly<{ offset: string; color: string }>[]
}>
export type WinningPaylinesProps = Readonly<{
  paylines: readonly PresentedPayline[]
  resultKey: number | string
  visible: boolean
  reducedMotion: boolean
  appearance?: PaylineAppearance
  boardSelector?: string
  reelsSelector?: string
  cellSelector?: string
  formatWinAmount?: (amountPoints: number) => string
  onActivePathChange?: (path: ActivePaidPath | null) => void
}>
type MeasuredPath = PaidPath & Readonly<{ points: string; labelX: number; labelY: number }>
type Geometry = Readonly<{ resultKey: number | string; width: number; height: number; paths: readonly MeasuredPath[] }>

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

/** Draws settled paths. Each game supplies its own appearance and board selectors. */
export function WinningPaylines({
  paylines, resultKey, visible, reducedMotion, appearance = {},
  boardSelector = '[data-payline-board]', reelsSelector = '[data-payline-reels]',
  cellSelector = '[data-reel-index][data-row-index]', formatWinAmount, onActivePathChange,
}: WinningPaylinesProps) {
  const { labelInset = 64, labelOffset = 16, labelMinimumY = 26 } = appearance
  const gradientId = `payline-amount-${useId().replaceAll(':', '-')}`
  const overlay = useRef<SVGSVGElement>(null)
  const paidPaths = useMemo(() => getPaidPaths(paylines), [paylines])
  const [geometry, setGeometry] = useState<Geometry | null>(null)
  const [cycle, setCycle] = useState({ resultKey, index: 0 })
  const css = (suffix = '') => `winning-paylines${suffix}${appearance.classPrefix ? ` ${appearance.classPrefix}${suffix}` : ''}`

  useLayoutEffect(() => {
    if (!visible || paidPaths.length === 0) return
    const svg = overlay.current
    const board = svg?.closest(boardSelector)
    const reels = board?.querySelector(reelsSelector)
    if (!svg || !board || !reels) return
    const measure = () => {
      const bounds = svg.getBoundingClientRect()
      if (bounds.width <= 0 || bounds.height <= 0) { setGeometry(null); return }
      const centers = new Map<string, readonly [number, number]>()
      for (const cell of reels.querySelectorAll<HTMLElement>(cellSelector)) {
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
        return [{ ...path, points: points.map(point => point.join(',')).join(' '),
          labelX: Math.max(labelInset, Math.min(bounds.width - labelInset, labelX)),
          labelY: Math.max(labelMinimumY, labelY - labelOffset) }]
      })
      const next = { resultKey, width: bounds.width, height: bounds.height, paths }
      setGeometry(previous => JSON.stringify(previous) === JSON.stringify(next) ? previous : next)
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(board)
    observer?.observe(reels)
    window.addEventListener('resize', measure)
    return () => { observer?.disconnect(); window.removeEventListener('resize', measure) }
  }, [boardSelector, cellSelector, labelInset, labelMinimumY, labelOffset, paidPaths, reelsSelector, resultKey, visible])

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
  return <svg ref={overlay} className={css()} aria-hidden="true" focusable="false"
    data-palette={appearance.variant} data-reduced-motion={reducedMotion ? 'true' : undefined}
    viewBox={`0 0 ${currentGeometry?.width ?? 1} ${currentGeometry?.height ?? 1}`} preserveAspectRatio="none">
    {appearance.amountGradient && <defs><linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
      {appearance.amountGradient.map(stop => <stop key={stop.offset} offset={stop.offset} stopColor={stop.color} />)}
    </linearGradient></defs>}
    {path && <g key={`${resultKey}:${path.id}`} data-payline-id={path.paylineId} data-points={path.points}>
      {appearance.steadyTrail && <polyline className={`${css('__trail')} ${appearance.steadyTrailClassName ?? ''}`}
        points={path.points} vectorEffect="non-scaling-stroke" strokeWidth={3.5} />}
      {appearance.reducedMotionTrail && reducedMotion && <polyline className={css('__trail')}
        points={path.points} vectorEffect="non-scaling-stroke" strokeWidth={3} strokeOpacity={0.85} />}
      <g className={css('__glint')}>
        {[[24, 9, .07], [19, 6, .12], [14, 4, .23], [9, 2.8, .44], [4, 1.6, .85]].map(([length, width, opacity], index) =>
          <polyline key={length} className={`${css('__trail')}${index < 2 ? ` ${css('__trail--soft')}` : ''}`}
            points={path.points} pathLength="100" vectorEffect="non-scaling-stroke"
            strokeWidth={width} strokeOpacity={opacity} strokeDasharray={`${length} 200`} strokeDashoffset={length}>
            {!reducedMotion && <animate attributeName="stroke-dashoffset" from={length} to={length - 100} dur="2.2s" repeatCount="indefinite" />}
          </polyline>)}
        <g>
          <ellipse className={css('__bloom')} cx="0" cy="0" rx="7" ry="3" />
          <ellipse className={css('__core')} cx="0" cy="0" rx="2.8" ry="1.4" />
          {!reducedMotion && <animateMotion dur="2.2s" repeatCount="indefinite" rotate="auto" path={`M${path.points.split(' ').join(' L')}`} />}
        </g>
      </g>
      {formatWinAmount && <text className={css('__amount')} x={path.labelX} y={path.labelY}
        textAnchor="middle" dominantBaseline={appearance.labelBaseline}
        style={appearance.amountGradient ? { fill: `url(#${gradientId})` } : undefined}>{formatWinAmount(path.amountPoints)}</text>}
    </g>}
  </svg>
}

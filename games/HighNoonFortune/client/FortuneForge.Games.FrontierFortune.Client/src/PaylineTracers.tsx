import { useId } from 'react'
import { PAYLINE_VIEWBOX, winTraceGeometry } from './paylineGeometry.js'
import type { FrontierWin } from './types.js'
import './paylines.css'

export type PaylineTracersProps = { wins: FrontierWin[]; activeLine: number | null }

/** Decorative traces of authoritative wins. The parent owns timing and announcements. */
export function PaylineTracers({ wins, activeLine }: PaylineTracersProps) {
  const glowId = `frontier-payline-glow-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  const traces = wins.map(win => ({ line: win.line, ...winTraceGeometry(win) }))
  if (traces.length === 0) return null

  return <svg className="frontier-payline-tracers"
    viewBox={`0 0 ${PAYLINE_VIEWBOX.width} ${PAYLINE_VIEWBOX.height}`}
    preserveAspectRatio="none" aria-hidden="true" focusable="false" pointerEvents="none">
    <defs>
      <filter id={glowId} x="-50%" y="-50%" width="200%" height="200%" colorInterpolationFilters="sRGB">
        <feGaussianBlur stdDeviation="4" result="glow" />
        <feMerge><feMergeNode in="glow" /><feMergeNode in="SourceGraphic" /></feMerge>
      </filter>
    </defs>
    <g className="frontier-payline-tracers__all">
      {traces.map(trace => <polyline key={trace.line} points={trace.points}
        className="frontier-payline-tracers__rest" vectorEffect="non-scaling-stroke" />)}
    </g>
    {traces.filter(trace => trace.line === activeLine).map(trace => <g key={`active-${trace.line}`}>
      <g className="frontier-payline-tracers__halos">
        {trace.cells.map((cell, index) => <circle key={`${cell.column}-${cell.row}-${index}`}
          cx={cell.x} cy={cell.y} r="69" vectorEffect="non-scaling-stroke" />)}
      </g>
      <polyline className="frontier-payline-tracers__active-line" points={trace.points}
        pathLength="1" vectorEffect="non-scaling-stroke" filter={`url(#${glowId})`} />
      <polyline className="frontier-payline-tracers__core" points={trace.points}
        pathLength="1" vectorEffect="non-scaling-stroke" />
    </g>)}
  </svg>
}

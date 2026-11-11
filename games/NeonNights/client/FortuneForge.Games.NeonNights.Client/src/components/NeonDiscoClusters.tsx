import { useId, useLayoutEffect, useState, type RefObject } from 'react'
import type { PaylinePayout } from '@fortuneforge/slot-client-core'
import './NeonDiscoClusters.css'

export type NeonDiscoClustersProps = {
  cabinetRef: RefObject<HTMLDivElement | null>
  clusters: readonly PaylinePayout[]
  resultKey: string | number
  visible: boolean
  formatAmount: (points: number) => string
  reducedMotion?: boolean
}

type ClusterLabel = { id: number; amountPoints: number; x: number; y: number }
type Geometry = { resultKey: string | number; width: number; height: number; labels: ClusterLabel[] }

export function NeonDiscoClusters({
  cabinetRef, clusters, resultKey, visible, formatAmount, reducedMotion = false,
}: NeonDiscoClustersProps) {
  const gradientId = `neon-disco-cluster-gold-${useId().replaceAll(':', '-')}`
  const [geometry, setGeometry] = useState<Geometry | null>(null)

  useLayoutEffect(() => {
    const cabinet = cabinetRef.current
    if (!visible || clusters.length === 0 || !cabinet) return

    const measure = () => {
      const bounds = cabinet.getBoundingClientRect()
      if (bounds.width <= 0 || bounds.height <= 0) {
        setGeometry(null)
        return
      }
      const cells = new Map<string, { x: number; y: number; height: number }>()
      for (const cell of cabinet.querySelectorAll<HTMLElement>('.slot-symbol[data-reel-index][data-row-index]')) {
        const rect = cell.getBoundingClientRect()
        if (rect.width <= 0 || rect.height <= 0) continue
        cells.set(`${cell.dataset.reelIndex}:${cell.dataset.rowIndex}`, {
          x: rect.left + rect.width / 2 - bounds.left,
          y: rect.top + rect.height / 2 - bounds.top,
          height: rect.height,
        })
      }

      const labels = clusters.flatMap(cluster => {
        if (cluster.amountPoints <= 0) return []
        const positions = new Map(cluster.matches.flatMap(match => match.match.positions)
          .map(position => [`${position.reel}:${position.row}`, position]))
        const points = [...positions.keys()].map(key => cells.get(key))
        if (points.length === 0 || points.some(point => point === undefined)) return []
        const measured = points as { x: number; y: number; height: number }[]
        const centerX = measured.reduce((sum, point) => sum + point.x, 0) / measured.length
        const centerY = measured.reduce((sum, point) => sum + point.y, 0) / measured.length
        const inset = Math.min(bounds.width / 2, Math.max(42, String(cluster.amountPoints).length * 6 + 24))
        return [{
          id: cluster.paylineId,
          amountPoints: cluster.amountPoints,
          x: Math.max(inset, Math.min(bounds.width - inset, centerX)),
          y: Math.max(25, Math.min(bounds.height - 20, centerY - Math.min(40, measured[0].height * 0.45))),
        }]
      })
      const next = { resultKey, width: bounds.width, height: bounds.height, labels }
      setGeometry(previous => JSON.stringify(previous) === JSON.stringify(next) ? previous : next)
    }

    measure()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(cabinet)
    const reels = cabinet.querySelector('.slot-machine__reels')
    if (reels) observer?.observe(reels)
    window.addEventListener('resize', measure)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [cabinetRef, clusters, resultKey, visible])

  if (!visible || clusters.length === 0) return null
  const current = geometry?.resultKey === resultKey ? geometry : null

  return <svg className="neon-disco-clusters" aria-hidden="true" focusable="false"
    data-reduced-motion={reducedMotion ? 'true' : undefined}
    viewBox={`0 0 ${current?.width ?? 1} ${current?.height ?? 1}`} preserveAspectRatio="none">
    <defs>
      <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" stopColor="#ffe54c" />
        <stop offset="24%" stopColor="#ffdb21" />
        <stop offset="49%" stopColor="#ffc00d" />
        <stop offset="76%" stopColor="#f99508" />
        <stop offset="100%" stopColor="#d45607" />
      </linearGradient>
    </defs>
    {current?.labels.map(label => <g key={`${resultKey}:${label.id}`} className="neon-disco-clusters__award"
      data-cluster-id={label.id}>
      <text className="neon-disco-clusters__amount" x={label.x} y={label.y}
        textAnchor="middle" dominantBaseline="central" fill={`url(#${gradientId})`}>
        {formatAmount(label.amountPoints)}
      </text>
    </g>)}
  </svg>
}

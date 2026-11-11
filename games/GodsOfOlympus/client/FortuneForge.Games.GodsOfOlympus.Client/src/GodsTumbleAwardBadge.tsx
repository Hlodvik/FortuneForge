import type { CSSProperties } from 'react'
import type { GridPosition } from '@fortuneforge/slot-client-core'

type GodsTumbleAwardBadgeProps = {
  positions: readonly GridPosition[]
  columns: number
  rows: number
  amount: string
}

export function GodsTumbleAwardBadge({ positions, columns, rows, amount }: GodsTumbleAwardBadgeProps) {
  const centerX = positions.reduce((sum, { reel }) => sum + reel + 0.5, 0) / positions.length
  const centerY = positions.reduce((sum, { row }) => sum + row + 0.5, 0) / positions.length

  return (
    <strong
      className="gods-tumble__award"
      style={{
        '--gods-tumble-award-x': `${centerX / columns * 100}%`,
        '--gods-tumble-award-y': `${centerY / rows * 100}%`,
      } as CSSProperties}
      aria-hidden="true"
    >
      {amount}
    </strong>
  )
}

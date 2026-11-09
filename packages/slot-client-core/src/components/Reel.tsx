import type { ReactNode } from 'react'

type ReelProps = {
  children?: ReactNode
  index: number
}

export function Reel({ children, index }: ReelProps) {
  return (
    <div className="slot-reel" role="group" aria-label={`Reel ${index + 1}`}>
      <div className="slot-reel__content">
        {children}
        <span className="slot-reel__flash" aria-hidden="true" />
      </div>
    </div>
  )
}

import type { ReactNode } from 'react'
import reelSurface from '../../../assets/slots/reel.png'

type ReelProps = {
  children?: ReactNode
  index: number
}

export function Reel({ children, index }: ReelProps) {
  return (
    <div className="slot-reel" role="group" aria-label={`Reel ${index + 1}`}>
      <img className="slot-reel__surface" src={reelSurface} alt="" aria-hidden="true" />
      <div className="slot-reel__content">{children}</div>
    </div>
  )
}

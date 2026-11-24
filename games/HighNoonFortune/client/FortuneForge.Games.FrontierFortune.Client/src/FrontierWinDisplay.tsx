import type { CSSProperties } from 'react'
import './winDisplay.css'

export type FrontierWinDisplayProps = {
  amount: string
  label: string
  detail?: string
}

export function FrontierWinDisplay({ amount, label, detail }: FrontierWinDisplayProps) {
  const sizing = { '--frontier-win-characters': Math.max(6, amount.length) } as CSSProperties

  return <div className="frontier-win-display" style={sizing}>
    <div className="frontier-win-display__award" role="status" aria-live="polite" aria-atomic="true">
      <span className="frontier-win-display__label">{label}</span>
      <strong className="frontier-win-display__amount">{amount}</strong>
    </div>
    {detail && <span className="frontier-win-display__detail" aria-hidden="true">{detail}</span>}
  </div>
}

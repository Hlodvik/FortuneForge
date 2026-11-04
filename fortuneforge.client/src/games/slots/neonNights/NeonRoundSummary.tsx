import { useLayoutEffect, useState, type RefObject } from 'react'
import { createPortal } from 'react-dom'

type CabinetBounds = { left: number; top: number; width: number; height: number }

type NeonRoundSummaryProps = {
  cabinetRef: RefObject<HTMLDivElement | null>
  title: string
  amount: string
  onDismiss: () => void
}

export function NeonRoundSummary({ cabinetRef, title, amount, onDismiss }: NeonRoundSummaryProps) {
  const [bounds, setBounds] = useState<CabinetBounds | null>(null)

  useLayoutEffect(() => {
    const cabinet = cabinetRef.current
    if (!cabinet) return
    const measure = () => {
      const rect = cabinet.getBoundingClientRect()
      setBounds({ left: rect.left, top: rect.top, width: rect.width, height: rect.height })
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(cabinet)
    window.addEventListener('resize', measure)
    window.addEventListener('scroll', measure, true)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
      window.removeEventListener('scroll', measure, true)
    }
  }, [cabinetRef])

  return createPortal(
    <div className="neon-round-summary-overlay" onClick={onDismiss}
      onKeyDown={(event) => { if (event.key === 'Escape') onDismiss() }}>
      <section
        className="neon-round-summary"
        role="dialog"
        aria-modal="true"
        aria-label={`${title} total won ${amount}`}
        style={bounds ?? undefined}
        onClick={(event) => event.stopPropagation()}
      >
        <button className="neon-round-summary__continue" type="button" autoFocus onClick={onDismiss}>
          <span>{title} complete</span>
          <small>Total won</small>
          <strong>{amount}</strong>
          <em>Continue</em>
        </button>
      </section>
    </div>,
    document.body,
  )
}

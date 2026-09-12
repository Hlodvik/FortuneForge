export type CardOutcomeTone = 'positive' | 'neutral' | 'caution'

type CardOutcomeSummaryProps = Readonly<{
  eyebrow: string
  title: string
  detail?: string
  nextAction?: string
  tone?: CardOutcomeTone
  className?: string
}>

export function CardOutcomeSummary({
  eyebrow,
  title,
  detail,
  nextAction,
  tone = 'neutral',
  className,
}: CardOutcomeSummaryProps) {
  return (
    <section
      className={`ff-card-outcome ff-card-outcome--${tone}${className ? ` ${className}` : ''}`}
      role="status"
      aria-atomic="true"
    >
      <span className="ff-card-outcome__eyebrow">{eyebrow}</span>
      <strong className="ff-card-outcome__title">{title}</strong>
      {detail && <span className="ff-card-outcome__detail">{detail}</span>}
      {nextAction && <small className="ff-card-outcome__next"><b>Next:</b> {nextAction}</small>}
    </section>
  )
}

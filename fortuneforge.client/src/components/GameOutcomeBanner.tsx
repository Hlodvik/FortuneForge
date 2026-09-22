import './gameOutcomeBanner.css'

export type GameOutcomeTone = 'win' | 'milestone' | 'neutral' | 'loss'
export type GameOutcomeSignificance = 'standard' | 'major'

type GameOutcomeBannerProps = Readonly<{
  className?: string
  detail?: string
  label: string
  nextAction?: string
  significance?: GameOutcomeSignificance
  title: string
  tone?: GameOutcomeTone
}>

/**
 * A compact, screen-reader-friendly summary of a completed game moment.
 * It intentionally describes presentation only; callers keep game state,
 * outcome calculations, and any money movement in their existing flows.
 */
export function GameOutcomeBanner({
  className = '',
  detail,
  label,
  nextAction,
  significance = 'standard',
  title,
  tone = 'neutral',
}: GameOutcomeBannerProps) {
  return (
    <section
      className={[
        'game-outcome-banner',
        `game-outcome-banner--${tone}`,
        `game-outcome-banner--${significance}`,
        className,
      ].filter(Boolean).join(' ')}
      role="status"
      aria-live="polite"
      aria-atomic="true"
    >
      <span className="game-outcome-banner__label">{label}</span>
      <strong className="game-outcome-banner__title">{title}</strong>
      {detail && <span className="game-outcome-banner__detail">{detail}</span>}
      {nextAction && (
        <small className="game-outcome-banner__next">
          <span aria-hidden="true">→</span> {nextAction}
        </small>
      )}
    </section>
  )
}

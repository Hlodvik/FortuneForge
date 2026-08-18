import type { MascotPhase, MascotSet } from './mascotTypes'
import './mascotCompanion.css'

type MascotCompanionProps = {
  actionKey?: number
  className?: string
  mascotSet: MascotSet
  phase?: MascotPhase
  successFrame?: number
  variant: 'game' | 'showcase'
}

export function MascotCompanion({
  actionKey = 0,
  className,
  mascotSet,
  phase = 'idle',
  successFrame = 0,
  variant,
}: MascotCompanionProps) {
  const { assets, timing } = mascotSet
  const platform = assets.platform
  const rootClassName = [
    'mascot-companion',
    `mascot-companion--${variant}`,
    variant === 'game' ? `mascot-companion--${phase}` : null,
    platform ? 'mascot-companion--has-platform' : 'mascot-companion--no-platform',
    className,
  ].filter(Boolean).join(' ')
  const successPoseIndex = timing.successTimeline[successFrame] ?? 0

  return (
    <div className={rootClassName} data-platform={platform?.kind ?? 'none'} aria-hidden="true">
      {platform && (
        <picture>
          <source media="(prefers-reduced-motion: reduce)" srcSet={platform.reducedMotion} />
          <img
            className={`mascot-companion__platform mascot-companion__platform--${platform.kind}`}
            src={platform.animated}
            alt=""
            draggable="false"
          />
        </picture>
      )}
      <div className="mascot-companion__breath">
        <img
          className="mascot-companion__sprite mascot-companion__sprite--idle"
          src={assets.idle}
          alt=""
          draggable="false"
          fetchPriority={variant === 'showcase' ? 'high' : undefined}
        />
      </div>

      {variant === 'game' && (
        <>
          <div className="mascot-companion__backflip" key={`backflip-${actionKey}`}>
            <img
              className="mascot-companion__sprite mascot-companion__sprite--backflip"
              src={assets.backflip}
              alt=""
              draggable="false"
            />
          </div>
          <div className="mascot-companion__action" key={actionKey}>
            <img
              className="mascot-companion__sprite mascot-companion__sprite--action"
              src={assets.action}
              alt=""
              draggable="false"
            />
          </div>
          <div className="mascot-companion__celebration" key={`success-${actionKey}`}>
            {assets.successPoses.map((source, poseIndex) => (
              <img
                className={`mascot-companion__sprite mascot-companion__sprite--success${successPoseIndex === poseIndex ? ' mascot-companion__sprite--success-active' : ''}`}
                src={source}
                alt=""
                draggable="false"
                key={source}
              />
            ))}
          </div>
        </>
      )}
    </div>
  )
}

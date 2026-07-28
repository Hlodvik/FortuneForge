import type { MascotPhase, MascotSet } from './WukongCompanion.config'

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
    'wukong-companion',
    `wukong-companion--${variant}`,
    variant === 'game' ? `wukong-companion--${phase}` : null,
    platform ? 'wukong-companion--has-platform' : 'wukong-companion--no-platform',
    className,
  ].filter(Boolean).join(' ')
  const successPoseIndex = timing.successTimeline[successFrame] ?? 0

  return (
    <div className={rootClassName} data-platform={platform?.kind ?? 'none'} aria-hidden="true">
      {platform && (
        <picture>
          <source media="(prefers-reduced-motion: reduce)" srcSet={platform.reducedMotion} />
          <img
            className={`wukong-companion__platform wukong-companion__platform--${platform.kind}`}
            src={platform.animated}
            alt=""
            draggable="false"
          />
        </picture>
      )}
      <div className="wukong-companion__breath">
        <img
          className="wukong-companion__sprite wukong-companion__sprite--idle"
          src={assets.idle}
          alt=""
          draggable="false"
          fetchPriority={variant === 'showcase' ? 'high' : undefined}
        />
      </div>

      {variant === 'game' && (
        <>
          <div className="wukong-companion__backflip" key={`backflip-${actionKey}`}>
            <img
              className="wukong-companion__sprite wukong-companion__sprite--backflip"
              src={assets.backflip}
              alt=""
              draggable="false"
            />
          </div>
          <div className="wukong-companion__action" key={actionKey}>
            <img
              className="wukong-companion__sprite wukong-companion__sprite--action"
              src={assets.action}
              alt=""
              draggable="false"
            />
          </div>
          <div className="wukong-companion__celebration" key={`success-${actionKey}`}>
            {assets.successPoses.map((source, poseIndex) => (
              <img
                className={`wukong-companion__sprite wukong-companion__sprite--success${successPoseIndex === poseIndex ? ' wukong-companion__sprite--success-active' : ''}`}
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

export const WukongCompanion = MascotCompanion

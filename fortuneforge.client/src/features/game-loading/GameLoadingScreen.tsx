import { useId, useState, type CSSProperties } from 'react'
import type { GameLoadingEntry } from './types'
import './gameLoadingScreen.css'

type LoadingStyle = CSSProperties & {
  '--game-loading-background': string
  '--game-loading-accent': string
}

/** Render only while the caller has genuine pending work; this view owns no timers. */
export function GameLoadingScreen({ entry, statusLabel }: { entry?: GameLoadingEntry; statusLabel?: string }) {
  const titleId = useId()
  const [failedImage, setFailedImage] = useState<string | null>(null)
  const showArtwork = Boolean(entry?.image && failedImage !== entry.image)
  const title = entry?.title ?? 'Your game'
  const style: LoadingStyle = {
    '--game-loading-background': entry?.background ?? '#100c19',
    '--game-loading-accent': entry?.accent ?? '#dbbc79',
  }

  return (
    <section
      className="ff-game-loading"
      aria-labelledby={titleId}
      data-game-loading={entry?.id ?? 'generic'}
      style={style}
    >
      <div className="ff-game-loading__content">
        <div className="ff-game-loading__artwork">
          <h1
            id={titleId}
            className={showArtwork ? 'ff-game-loading__sr-only' : 'ff-game-loading__fallback-title'}
          >
            {title}
          </h1>
          {showArtwork && entry && (
            <img
              className="ff-game-loading__image"
              src={entry.image}
              alt=""
              width={768}
              height={384}
              loading="eager"
              decoding="async"
              fetchPriority="high"
              style={{ objectPosition: entry.objectPosition ?? 'center' }}
              onError={() => setFailedImage(entry.image)}
            />
          )}
        </div>
        <p className="ff-game-loading__status" role="status" aria-live="polite" aria-atomic="true">
          <span className="ff-game-loading__indicator" aria-hidden="true" />
          <span>{statusLabel ?? (entry ? `Loading ${entry.title}…` : 'Loading game…')}</span>
        </p>
      </div>
    </section>
  )
}

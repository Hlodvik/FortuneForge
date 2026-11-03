import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { useSlotDialog } from '../../../features/slots/hooks/useSlotDialog'
import type { GridPosition } from '../../../features/slots/types/slots'

const symbolRevealMs = 1100
const clipTimeoutMs = 12_000
const clipFiles = {
  '6': 'car-special-plays.mp4',
  POWER: 'disco-ball-special-plays.mp4',
  SEAL_RAND: 'car-special-plays.mp4',
} as const

type NeonSpecialPlaysIntroProps = {
  cabinetRef: RefObject<HTMLDivElement | null>
  position: GridPosition
  symbol: keyof typeof clipFiles
  symbolImage: string
  volume: number
  onComplete: () => void
}

export function NeonSpecialPlaysIntro({ cabinetRef, position, symbol, symbolImage, volume, onComplete }: NeonSpecialPlaysIntroProps) {
  const [showClip, setShowClip] = useState(false)
  const dialogRef = useSlotDialog(true)
  const symbolRef = useRef<HTMLImageElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const completedRef = useRef(false)
  const finish = useCallback(() => {
    if (completedRef.current) return
    completedRef.current = true
    onComplete()
  }, [onComplete])

  useLayoutEffect(() => {
    const image = symbolRef.current
    const overlay = dialogRef.current?.getBoundingClientRect()
    const source = cabinetRef.current?.querySelector(
      `[data-reel-index="${position.reel}"][data-row-index="${position.row}"] .slot-symbol__image`,
    )?.getBoundingClientRect()
    if (!image || !overlay || !source?.width || !image.offsetWidth) return

    image.style.setProperty('--neon-special-intro-start-x', `${source.left + source.width / 2 - overlay.left - overlay.width / 2}px`)
    image.style.setProperty('--neon-special-intro-start-y', `${source.top + source.height / 2 - overlay.top - overlay.height / 2}px`)
    image.style.setProperty('--neon-special-intro-start-scale', String(source.width / image.offsetWidth))
  }, [cabinetRef, dialogRef, position])

  useEffect(() => {
    const timer = window.setTimeout(() => setShowClip(true), symbolRevealMs)
    return () => window.clearTimeout(timer)
  }, [])

  useEffect(() => {
    const video = videoRef.current
    if (!showClip || !video) return
    if (video.error) {
      finish()
      return
    }

    let active = true
    video.volume = volume
    video.muted = volume === 0
    void video.play().catch(async () => {
      if (!active) return
      video.muted = true
      try {
        await video.play()
      } catch {
        if (active) finish()
      }
    })
    const timer = window.setTimeout(finish, clipTimeoutMs)
    return () => {
      active = false
      window.clearTimeout(timer)
    }
  }, [finish, showClip, volume])

  return createPortal(
    <section
      className="neon-special-intro"
      data-trigger-symbol={symbol}
      ref={dialogRef}
      role="dialog"
      aria-modal="true"
      aria-label="Special Plays intro"
      tabIndex={-1}
    >
      {!showClip && <img
        className="neon-special-intro__symbol"
        ref={symbolRef}
        src={symbolImage}
        alt=""
        style={{ '--neon-special-intro-reveal-duration': `${symbolRevealMs}ms` } as CSSProperties}
      />}
      <video
        className="neon-special-intro__clip"
        ref={videoRef}
        src={`${import.meta.env.BASE_URL}assets/slots/neon-nights/${clipFiles[symbol]}`}
        preload="auto"
        playsInline
        hidden={!showClip}
        aria-hidden="true"
        onEnded={finish}
        onError={showClip ? finish : undefined}
      />
    </section>,
    document.body,
  )
}

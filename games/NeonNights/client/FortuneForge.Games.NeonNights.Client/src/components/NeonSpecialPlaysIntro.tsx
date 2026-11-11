import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { useSlotDialog, type GridPosition } from '@fortuneforge/slot-client-core'
import type { NeonTriggerSymbol } from '../types.js'
import carVideo from '../assets/video/car-special-plays.mp4'
import discoVideo from '../assets/video/disco-ball-special-plays-alpha.webm'
import discoVideoFallback from '../assets/video/disco-ball-special-plays-v4.mp4'

const clipTimeoutMs = 12_000
const discoMusicCrossfadeSeconds = 0.38
// The light at the edges of the clip blooms from 2.0s and peaks near 2.42s.
const discoFlareStartSeconds = 2
const discoFlarePeakSeconds = 2.42
// Visible ball bounds in the first video frame and the trigger artwork, in source pixels.
const discoFrame = { width: 1280, height: 720, ballX: 652.5, ballY: 391.5, ballDiameter: 460 }
const discoSymbolArt = { width: 320, ballX: 159.5, ballY: 173.5, ballDiameter: 228 }

export type DiscoFlareOrigin = { x: number; y: number }
export type DiscoFlare = DiscoFlareOrigin & { strength: number }

type DiscoIntroLayout = {
  clip: CSSProperties
  symbol: CSSProperties
  ballCenter: DiscoFlareOrigin
}

type NeonSpecialPlaysIntroProps = {
  cabinetRef: RefObject<HTMLDivElement | null>
  position: GridPosition
  symbol: NeonTriggerSymbol
  symbolImage: string
  volume: number
  reducedMotion: boolean
  onDiscoMusicStart?: (fadeInMs: number) => void
  onComplete: (flare?: DiscoFlare) => void
}

export function NeonSpecialPlaysIntro({ cabinetRef, position, symbol, symbolImage, volume, reducedMotion, onDiscoMusicStart, onComplete }: NeonSpecialPlaysIntroProps) {
  const [clipPrepared, setClipPrepared] = useState(false)
  const [videoReady, setVideoReady] = useState(false)
  const [discoLayout, setDiscoLayout] = useState<DiscoIntroLayout>()
  const isDisco = symbol === 'POWER'
  const growLayout = isDisco && clipPrepared && !reducedMotion ? discoLayout : undefined
  const dialogRef = useSlotDialog(true)
  const videoRef = useRef<HTMLVideoElement>(null)
  const videoFrame = useRef<number | null>(null)
  const videoFlareRef = useRef<HTMLDivElement>(null)
  const flareStrength = useRef(0)
  const musicStarted = useRef(false)
  const completedRef = useRef(false)
  const finish = useCallback((flare?: DiscoFlare) => {
    if (completedRef.current) return
    completedRef.current = true
    onComplete(flare)
  }, [onComplete])

  useLayoutEffect(() => {
    const cabinet = cabinetRef.current
    if (!isDisco || !cabinet) return

    const measure = () => {
      const { left, top, width, height } = cabinet.getBoundingClientRect()
      const frameScale = Math.min(width / discoFrame.width, height / discoFrame.height)
      if (frameScale <= 0) return

      const ballCenter = {
        x: left + width / 2 + (discoFrame.ballX - discoFrame.width / 2) * frameScale,
        y: top + height / 2 + (discoFrame.ballY - discoFrame.height / 2) * frameScale,
      }
      const symbolWidth = discoFrame.ballDiameter * frameScale * discoSymbolArt.width / discoSymbolArt.ballDiameter
      const symbolLeft = ballCenter.x + (0.5 - discoSymbolArt.ballX / discoSymbolArt.width) * symbolWidth
      const symbolTop = ballCenter.y + (0.5 - discoSymbolArt.ballY / discoSymbolArt.width) * symbolWidth
      const source = cabinet.querySelector(
        `[data-reel-index="${position.reel}"][data-row-index="${position.row}"] .slot-symbol__image`,
      )?.getBoundingClientRect()
      setDiscoLayout({
        clip: { left, top, width, height },
        symbol: {
          left: symbolLeft,
          top: symbolTop,
          width: symbolWidth,
          '--neon-special-intro-start-x': `${source ? source.left + source.width / 2 - symbolLeft : 0}px`,
          '--neon-special-intro-start-y': `${source ? source.top + source.height / 2 - symbolTop : 0}px`,
          '--neon-special-intro-start-scale': source?.width ? source.width / symbolWidth : 0.2,
        } as CSSProperties,
        ballCenter,
      })
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
  }, [cabinetRef, isDisco, position])

  useEffect(() => {
    const video = videoRef.current
    return () => {
      if (videoFrame.current !== null) video?.cancelVideoFrameCallback(videoFrame.current)
    }
  }, [])

  useEffect(() => {
    const timer = window.setTimeout(() => finish(), clipTimeoutMs)
    return () => window.clearTimeout(timer)
  }, [finish])

  const startPlayback = useCallback(() => {
    const video = videoRef.current
    if (!video || !video.paused || completedRef.current) return
    if (video.error) {
      finish()
      return
    }

    video.volume = volume
    video.muted = volume === 0
    void video.play().catch(async () => {
      if (!video.isConnected || completedRef.current) return
      video.muted = true
      try {
        await video.play()
      } catch {
        if (video.isConnected) finish()
      }
    })
  }, [finish, volume])

  useEffect(() => {
    if (!isDisco || reducedMotion) startPlayback()
  }, [isDisco, reducedMotion, startPlayback])

  function syncDiscoFrame(video: HTMLVideoElement, seconds: number) {
    const rise = Math.min(1, Math.max(0, (seconds - discoFlareStartSeconds) / (discoFlarePeakSeconds - discoFlareStartSeconds)))
    const fall = Math.max(0, seconds - discoFlarePeakSeconds)
    const strength = 0.88 * rise * (1 - Math.min(0.3, fall * 0.5))
    flareStrength.current = strength
    videoFlareRef.current?.style.setProperty('--disco-flare-strength', String(strength))
    if (rise > 0) {
      video.style.setProperty('--neon-disco-clip-opacity', String((1 - rise) ** 2))
    }

    if (!Number.isFinite(video.duration) || video.duration <= 0) return
    const remaining = Math.max(0, video.duration - seconds)
    const progress = 1 - remaining / video.duration
    dialogRef.current?.style.setProperty('--neon-disco-intro-progress', String(progress))
    if (remaining > discoMusicCrossfadeSeconds) return
    if (!musicStarted.current) {
      musicStarted.current = true
      onDiscoMusicStart?.(remaining * 1000)
    }
    video.volume = volume * remaining / discoMusicCrossfadeSeconds
  }

  function handleVideoPlaying(video: HTMLVideoElement) {
    if (!video.requestVideoFrameCallback) {
      setVideoReady(true)
      return
    }
    if (videoFrame.current !== null) video.cancelVideoFrameCallback(videoFrame.current)
    let firstFrame = true
    const drawFrame: VideoFrameRequestCallback = (_, frame) => {
      videoFrame.current = null
      if (completedRef.current) return
      if (firstFrame) {
        firstFrame = false
        setVideoReady(true)
      }
      if (isDisco) {
        syncDiscoFrame(video, frame.mediaTime)
        videoFrame.current = video.requestVideoFrameCallback(drawFrame)
      }
    }
    videoFrame.current = video.requestVideoFrameCallback(drawFrame)
  }

  return createPortal(
    <section
      className="neon-special-intro"
      data-trigger-symbol={symbol}
      data-disco-grow={growLayout ? 'true' : undefined}
      data-video-ready={videoReady ? 'true' : undefined}
      ref={dialogRef}
      role="dialog"
      aria-modal="true"
      aria-label="Special Plays intro"
      tabIndex={-1}
    >
      {isDisco && discoLayout && <div
        className="neon-disco-intro-flare neon-disco-intro-flare--video"
        ref={videoFlareRef}
        aria-hidden="true"
        style={{
          '--disco-flare-x': `${discoLayout.ballCenter.x}px`,
          '--disco-flare-y': `${discoLayout.ballCenter.y}px`,
        } as CSSProperties}
      />}
      {growLayout && <img
        className="neon-special-intro__symbol"
        src={symbolImage}
        alt=""
        style={growLayout.symbol}
        onAnimationEnd={event => {
          if (event.animationName === 'neon-special-intro-grow') startPlayback()
        }}
      />}
      <video
        className="neon-special-intro__clip"
        style={discoLayout?.clip}
        ref={videoRef}
        src={isDisco ? undefined : carVideo}
        preload="auto"
        playsInline
        aria-hidden="true"
        onLoadedData={() => setClipPrepared(true)}
        onPlaying={event => handleVideoPlaying(event.currentTarget)}
        onTimeUpdate={event => {
          const video = event.currentTarget
          if (isDisco && !video.requestVideoFrameCallback) syncDiscoFrame(video, video.currentTime)
        }}
        onEnded={() => {
          const origin = discoLayout?.ballCenter
          finish(isDisco && videoReady && origin && flareStrength.current > 0
            ? { ...origin, strength: flareStrength.current }
            : undefined)
        }}
        onError={() => finish()}
      >
        {isDisco && <>
          <source src={discoVideo} type="video/webm" />
          <source src={discoVideoFallback} type="video/mp4" />
        </>}
      </video>
    </section>,
    document.body,
  )
}

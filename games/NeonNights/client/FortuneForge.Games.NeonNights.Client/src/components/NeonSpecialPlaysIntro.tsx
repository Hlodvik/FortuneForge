import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { useSlotDialog } from '@fortuneforge/slot-client-core'
import type { NeonTriggerSymbol } from '../types.js'
import carVideo from '../assets/video/car-special-plays.mp4'
import discoVideo from '../assets/video/disco-ball-special-plays-alpha.webm'
import discoVideoFallback from '../assets/video/disco-ball-special-plays-v4.mp4'
import discoRiser from '../assets/audio/disco-intro-riser.ogg'

const clipTimeoutMs = 12_000
const discoMusicStartSeconds = 2.53
const discoMusicFadeInMs = 30
const discoFlareStartSeconds = 2
const discoFlarePeakSeconds = 2.42
const discoRiserGain = 0.65
// Ball center in the first video frame, in source pixels.
const discoFrame = { width: 1280, height: 720, ballX: 652.5, ballY: 391.5 }

export type DiscoFlareOrigin = { x: number; y: number }
export type DiscoFlare = DiscoFlareOrigin & { strength: number }

type DiscoIntroLayout = {
  clip: CSSProperties
  ballCenter: DiscoFlareOrigin
}

type NeonSpecialPlaysIntroProps = {
  cabinetRef: RefObject<HTMLDivElement | null>
  symbol: NeonTriggerSymbol
  volume: number
  onDiscoMusicStart?: (fadeInMs: number) => void
  onComplete: (flare?: DiscoFlare) => void
}

export function NeonSpecialPlaysIntro({ cabinetRef, symbol, volume, onDiscoMusicStart, onComplete }: NeonSpecialPlaysIntroProps) {
  const [videoReady, setVideoReady] = useState(false)
  const [discoLayout, setDiscoLayout] = useState<DiscoIntroLayout>()
  const isDisco = symbol === 'POWER'
  const dialogRef = useSlotDialog(true)
  const videoRef = useRef<HTMLVideoElement>(null)
  const riserRef = useRef<HTMLAudioElement>(null)
  const riserStarted = useRef(false)
  const volumeRef = useRef(volume)
  const videoFrame = useRef<number | null>(null)
  const discoPlaybackFrame = useRef<number | null>(null)
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
      setDiscoLayout({ clip: { left, top, width, height }, ballCenter })
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
  }, [cabinetRef, isDisco])

  useEffect(() => {
    const video = videoRef.current
    return () => {
      if (videoFrame.current !== null) video?.cancelVideoFrameCallback(videoFrame.current)
      if (discoPlaybackFrame.current !== null) window.cancelAnimationFrame(discoPlaybackFrame.current)
    }
  }, [])

  useEffect(() => {
    const timer = window.setTimeout(() => finish(), clipTimeoutMs)
    return () => window.clearTimeout(timer)
  }, [finish])

  useEffect(() => {
    volumeRef.current = volume
    const riser = riserRef.current
    if (riser) riser.volume = volume * discoRiserGain
    const video = videoRef.current
    if (!video) return
    video.volume = volume
    video.muted = isDisco || volume === 0
  }, [isDisco, volume])

  const startRiser = useCallback(() => {
    const riser = riserRef.current
    if (!riser || riserStarted.current || completedRef.current) return
    riserStarted.current = true
    riser.volume = volumeRef.current * discoRiserGain
    void riser.play().catch(() => {})
  }, [])

  const startPlayback = useCallback(() => {
    const video = videoRef.current
    if (!video || !video.paused || completedRef.current) return
    if (video.error) {
      finish()
      return
    }

    video.volume = volumeRef.current
    video.muted = isDisco || volumeRef.current === 0
    void video.play().catch(async () => {
      if (!video.isConnected || completedRef.current) return
      video.muted = true
      try {
        await video.play()
      } catch {
        if (video.isConnected) finish()
      }
    })
  }, [finish, isDisco])

  useEffect(() => {
    startPlayback()
  }, [startPlayback])

  function syncDiscoFrame(video: HTMLVideoElement, seconds: number) {
    if (!musicStarted.current && seconds >= discoMusicStartSeconds && onDiscoMusicStart) {
      musicStarted.current = true
      onDiscoMusicStart(discoMusicFadeInMs)
    }

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
  }

  function handleVideoPlaying(video: HTMLVideoElement) {
    if (isDisco && discoPlaybackFrame.current === null) {
      const updateDiscoPlayback = () => {
        discoPlaybackFrame.current = null
        if (completedRef.current || video.paused || video.ended) return
        syncDiscoFrame(video, video.currentTime)
        discoPlaybackFrame.current = window.requestAnimationFrame(updateDiscoPlayback)
      }
      discoPlaybackFrame.current = window.requestAnimationFrame(updateDiscoPlayback)
    }
    if (!video.requestVideoFrameCallback) {
      setVideoReady(true)
      if (isDisco) startRiser()
      return
    }
    if (videoFrame.current !== null) video.cancelVideoFrameCallback(videoFrame.current)
    videoFrame.current = video.requestVideoFrameCallback(() => {
      videoFrame.current = null
      if (!completedRef.current) {
        setVideoReady(true)
        if (isDisco) startRiser()
      }
    })
  }

  return createPortal(
    <section
      className="neon-special-intro"
      data-trigger-symbol={symbol}
      data-video-ready={videoReady ? 'true' : undefined}
      ref={dialogRef}
      role="dialog"
      aria-modal="true"
      aria-label="Special Plays intro"
      tabIndex={-1}
    >
      {isDisco && <audio ref={riserRef} src={discoRiser} preload="auto" />}
      {isDisco && discoLayout && <div
        className="neon-disco-intro-flare neon-disco-intro-flare--video"
        ref={videoFlareRef}
        aria-hidden="true"
        style={{
          '--disco-flare-x': `${discoLayout.ballCenter.x}px`,
          '--disco-flare-y': `${discoLayout.ballCenter.y}px`,
        } as CSSProperties}
      />}
      <video
        className="neon-special-intro__clip"
        style={discoLayout?.clip}
        ref={videoRef}
        src={isDisco ? undefined : carVideo}
        preload="auto"
        playsInline
        aria-hidden="true"
        onPlaying={event => handleVideoPlaying(event.currentTarget)}
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

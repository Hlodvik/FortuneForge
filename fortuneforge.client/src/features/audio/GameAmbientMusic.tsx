import { useCallback, useEffect, useRef, useState } from 'react'
import asianDragonSource from '../../assets/slots/ambient/asian-dragon.mp3'
import carnivalLightsSource from '../../assets/slots/ambient/carnival-lights.mp3'
import casinoRockSource from '../../assets/slots/ambient/casino-rock.mp3'
import countryTrailSource from '../../assets/slots/ambient/country-trail.mp3'
import cyberNightSource from '../../assets/slots/ambient/cyber-night.mp3'
import forestJourneySource from '../../assets/slots/ambient/forest-journey.mp3'
import hauntedChimesSource from '../../assets/slots/ambient/haunted-chimes.mp3'
import nordicAtmosphereSource from '../../assets/slots/ambient/nordic-atmosphere.mp3'
import './GameAmbientMusic.css'

export const AMBIENT_GAME_MUSIC = {
  asteroids: { source: cyberNightSource, volume: 0.04 },
  flappy: { source: forestJourneySource, volume: 0.035 },
  'horse-flight': { source: countryTrailSource, volume: 0.035 },
  snake: { source: nordicAtmosphereSource, volume: 0.035 },
  '2048': { source: casinoRockSource, volume: 0.03 },
  'drop-merge': { source: carnivalLightsSource, volume: 0.035 },
  craps: { source: casinoRockSource, volume: 0.035 },
  'liars-dice': { source: hauntedChimesSource, volume: 0.028 },
  'sic-bo': { source: asianDragonSource, volume: 0.035 },
} as const

export type AmbientGameId = keyof typeof AMBIENT_GAME_MUSIC

const MUTE_KEY = 'fortune-forge.ambient-game-music-muted'

export function GameAmbientMusic({ game }: Readonly<{ game: AmbientGameId }>) {
  const { source, volume } = AMBIENT_GAME_MUSIC[game]
  const audioRef = useRef<HTMLAudioElement | null>(null)
  const [isMuted, setIsMuted] = useState(readMutedPreference)

  const beginPlayback = useCallback(() => {
    const audio = audioRef.current
    if (audio === null || isMuted) return
    audio.volume = volume
    audio.muted = false
    void audio.play().catch(() => undefined)
  }, [isMuted, volume])

  useEffect(() => {
    const beginFromPlayerAction = () => beginPlayback()
    window.addEventListener('pointerdown', beginFromPlayerAction, { capture: true })
    window.addEventListener('keydown', beginFromPlayerAction, { capture: true })
    return () => {
      window.removeEventListener('pointerdown', beginFromPlayerAction, { capture: true })
      window.removeEventListener('keydown', beginFromPlayerAction, { capture: true })
    }
  }, [beginPlayback])

  useEffect(() => {
    const audio = audioRef.current
    if (audio === null) return
    audio.volume = volume
    audio.muted = isMuted
    if (!isMuted) void audio.play().catch(() => undefined)
    return () => {
      audio.pause()
      audio.currentTime = 0
    }
  }, [isMuted, source, volume])

  const toggleMuted = () => {
    const nextMuted = !isMuted
    setIsMuted(nextMuted)
    try { window.localStorage.setItem(MUTE_KEY, String(nextMuted)) } catch { /* Storage is optional. */ }
    const audio = audioRef.current
    if (audio === null) return
    audio.muted = nextMuted
    if (!nextMuted) {
      audio.volume = volume
      void audio.play().catch(() => undefined)
    }
  }

  return <>
    <audio aria-hidden="true" loop preload="metadata" ref={audioRef} src={source} />
    <button
      className="game-ambient-toggle"
      type="button"
      aria-label={isMuted ? 'Enable background music' : 'Mute background music'}
      aria-pressed={!isMuted}
      onClick={toggleMuted}
    >
      {isMuted ? 'Music off' : 'Music on'}
    </button>
  </>
}

function readMutedPreference(): boolean {
  if (typeof window === 'undefined') return false
  try { return window.localStorage.getItem(MUTE_KEY) === 'true' } catch { return false }
}

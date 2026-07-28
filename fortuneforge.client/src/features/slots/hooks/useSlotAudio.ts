import { useCallback, useEffect, useRef, useState } from 'react'
import type {
  SlotSoundCue,
  SlotSoundCueId,
  SlotSoundSet,
} from '../config/soundSets'

export type AudioMode = 'all' | 'muted' | 'results-only'
export type AudioPreferences = {
  mode: AudioMode
  volume: number
}

const AUDIO_PREFERENCES_KEY = 'fortune-forge.audio-preferences'
const DEFAULT_AUDIO_PREFERENCES: AudioPreferences = { mode: 'all', volume: 65 }

function loadAudioPreferences(): AudioPreferences {
  try {
    const savedPreferences = JSON.parse(
      window.localStorage.getItem(AUDIO_PREFERENCES_KEY) ?? 'null',
    ) as Partial<AudioPreferences> | null
    const savedMode = savedPreferences?.mode
    const savedVolume = savedPreferences?.volume

    return {
      mode: savedMode === 'muted' || savedMode === 'results-only' ? savedMode : 'all',
      volume: typeof savedVolume === 'number'
        ? Math.min(100, Math.max(0, Math.round(savedVolume)))
        : DEFAULT_AUDIO_PREFERENCES.volume,
    }
  } catch {
    return DEFAULT_AUDIO_PREFERENCES
  }
}

function stopAudio(audio: HTMLAudioElement) {
  audio.pause()
  audio.currentTime = 0
}

// Owns browser Audio objects and user preferences so SlotsPage only requests
// semantic cues from the selected sound set.
export function useSlotAudio(soundSet: SlotSoundSet) {
  const [preferences, setPreferences] = useState(loadAudioPreferences)
  const preferencesRef = useRef(preferences)
  const oneShotsRef = useRef<Map<HTMLAudioElement, SlotSoundCue>>(new Map())
  const loopsRef = useRef<Map<SlotSoundCueId, HTMLAudioElement>>(new Map())

  const isCueAllowed = useCallback((cue: SlotSoundCue) => {
    const { mode } = preferencesRef.current
    return mode !== 'muted' && (mode === 'all' || cue.category === 'result')
  }, [])

  const updatePreferences = useCallback(
    (update: (current: AudioPreferences) => AudioPreferences) => {
      setPreferences((current) => {
        const next = update(current)
        // Playback callbacks may run before React effects, so keep the ref current
        // at the same moment the preference state changes.
        preferencesRef.current = next
        return next
      })
    },
    [],
  )

  useEffect(() => {
    const volumeScale = preferences.volume / 100

    oneShotsRef.current.forEach((cue, audio) => {
      if (!isCueAllowed(cue)) {
        stopAudio(audio)
        oneShotsRef.current.delete(audio)
        return
      }
      audio.volume = cue.baseVolume * volumeScale
    })

    loopsRef.current.forEach((audio, cueId) => {
      const cue = soundSet.cues[cueId]
      if (!isCueAllowed(cue)) {
        stopAudio(audio)
        loopsRef.current.delete(cueId)
        return
      }
      audio.volume = cue.baseVolume * volumeScale
    })

    try {
      window.localStorage.setItem(AUDIO_PREFERENCES_KEY, JSON.stringify(preferences))
    } catch {
      // Audio remains usable when private browsing or storage policy blocks persistence.
    }
  }, [isCueAllowed, preferences, soundSet])

  useEffect(() => () => {
    oneShotsRef.current.forEach((_, audio) => stopAudio(audio))
    loopsRef.current.forEach((audio) => stopAudio(audio))
    oneShotsRef.current.clear()
    loopsRef.current.clear()
  }, [soundSet])

  const playCue = useCallback((cueId: SlotSoundCueId) => {
    const cue = soundSet.cues[cueId]
    if (!isCueAllowed(cue)) {
      return
    }

    const audio = new Audio(cue.source)
    const releaseAudio = () => oneShotsRef.current.delete(audio)
    audio.volume = cue.baseVolume * preferencesRef.current.volume / 100
    oneShotsRef.current.set(audio, cue)
    audio.addEventListener('ended', releaseAudio, { once: true })
    void audio.play().catch(releaseAudio)
  }, [isCueAllowed, soundSet])

  const playSequence = useCallback((cueIds: readonly SlotSoundCueId[]) => {
    cueIds.forEach(playCue)
  }, [playCue])

  const stopResultCues = useCallback(() => {
    oneShotsRef.current.forEach((cue, audio) => {
      if (cue.category === 'result') {
        stopAudio(audio)
        oneShotsRef.current.delete(audio)
      }
    })
  }, [])

  const stopLoop = useCallback((cueId: SlotSoundCueId) => {
    const audio = loopsRef.current.get(cueId)
    if (audio === undefined) {
      return
    }
    stopAudio(audio)
    loopsRef.current.delete(cueId)
  }, [])

  const startLoop = useCallback((cueId: SlotSoundCueId) => {
    stopLoop(cueId)
    const cue = soundSet.cues[cueId]
    if (!isCueAllowed(cue)) {
      return
    }

    const audio = new Audio(cue.source)
    audio.loop = cue.loop ?? true
    audio.volume = cue.baseVolume * preferencesRef.current.volume / 100
    loopsRef.current.set(cueId, audio)
    void audio.play().catch(() => {
      // Do not let a rejected promise from an older loop remove its replacement.
      if (loopsRef.current.get(cueId) === audio) {
        loopsRef.current.delete(cueId)
      }
    })
  }, [isCueAllowed, soundSet, stopLoop])

  const setVolume = useCallback((volume: number) => {
    updatePreferences((current) => ({
      ...current,
      volume: Math.min(100, Math.max(0, Math.round(volume))),
    }))
  }, [updatePreferences])

  const toggleMuted = useCallback(() => {
    updatePreferences((current) => ({
      ...current,
      mode: current.mode === 'muted' ? 'all' : 'muted',
    }))
  }, [updatePreferences])

  const toggleResultsOnly = useCallback(() => {
    updatePreferences((current) => ({
      ...current,
      mode: current.mode === 'results-only' ? 'all' : 'results-only',
    }))
  }, [updatePreferences])

  return {
    preferences,
    playCue,
    playSequence,
    setVolume,
    startLoop,
    stopLoop,
    stopResultCues,
    toggleMuted,
    toggleResultsOnly,
  }
}

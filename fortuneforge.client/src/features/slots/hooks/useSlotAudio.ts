import { useCallback, useEffect, useRef, useState } from 'react'
import type {
  SlotSoundCue,
  SlotSoundCueId,
  SlotSoundSet,
} from '../config/soundSets'

import { loadAudioPreferences, saveAudioPreferences, type AudioPreferences } from '../audio/audioPreferences'
export type { AudioMode, AudioPreferences } from '../audio/audioPreferences'

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

  const isCueAllowed = useCallback((cue: SlotSoundCue | undefined): cue is SlotSoundCue => {
    const { mode } = preferencesRef.current
    return cue !== undefined && cue.enabled !== false && mode !== 'muted' && (mode === 'all' || cue.category === 'result')
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

    saveAudioPreferences(preferences)
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

    if (soundSet.events.themeSuite && Object.values(soundSet.events.collectionArrival ?? {}).includes(cueId)) {
      // At most one live accent for each track, including across fast results.
      oneShotsRef.current.forEach((activeCue, activeAudio) => {
        if (activeCue === cue) { stopAudio(activeAudio); oneShotsRef.current.delete(activeAudio) }
      })
    }

    const audio = new Audio(cue.source)
    const releaseAudio = () => oneShotsRef.current.delete(audio)
    audio.volume = cue.baseVolume * preferencesRef.current.volume / 100
    oneShotsRef.current.set(audio, cue)
    audio.addEventListener('ended', releaseAudio, { once: true })
    void audio.play().catch(releaseAudio)
  }, [isCueAllowed, soundSet])

  const playSequence = useCallback((cueIds: readonly SlotSoundCueId[]) => {
    const selected = soundSet.events.themeSuite ? [...new Set(cueIds)] : cueIds
    selected.forEach(playCue)
  }, [playCue, soundSet])

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
    const cue = soundSet.cues[cueId]
    if (!isCueAllowed(cue)) {
      return
    }

    if (soundSet.events.themeSuite && (cueId === soundSet.events.ambience || cueId === soundSet.events.specialAmbience)) {
      const musicIds = [soundSet.events.ambience, soundSet.events.specialAmbience]
      loopsRef.current.forEach((otherAudio, otherId) => {
        if (otherId !== cueId && musicIds.includes(otherId)) {
          stopAudio(otherAudio); loopsRef.current.delete(otherId)
        }
      })
    }

    const existingAudio = loopsRef.current.get(cueId)
    const audio = existingAudio ?? new Audio(cue.source)
    audio.loop = cue.loop ?? true
    audio.volume = cue.baseVolume * preferencesRef.current.volume / 100
    if (existingAudio === undefined) {
      loopsRef.current.set(cueId, audio)
    }
    void audio.play().catch(() => {
      // Keep the same element available until this rejection settles so a
      // trusted interaction can retry play() instead of racing a replacement.
      if (loopsRef.current.get(cueId) === audio && audio.paused) {
        loopsRef.current.delete(cueId)
      }
    })
  }, [isCueAllowed, soundSet])

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

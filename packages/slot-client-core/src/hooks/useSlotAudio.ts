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

type GaplessLoop = { source: AudioBufferSourceNode; gain: GainNode; startedAt: number; context: AudioContext }

// Owns browser Audio objects and user preferences so SlotsPage only requests
// semantic cues from the selected sound set.
export function useSlotAudio(soundSet: SlotSoundSet) {
  const [preferences, setPreferences] = useState(loadAudioPreferences)
  const preferencesRef = useRef(preferences)
  const oneShotsRef = useRef<Map<HTMLAudioElement, SlotSoundCue>>(new Map())
  const loopsRef = useRef<Map<SlotSoundCueId, HTMLAudioElement>>(new Map())
  const gaplessLoopsRef = useRef<Map<SlotSoundCueId, GaplessLoop>>(new Map())
  const pendingGaplessLoopsRef = useRef<Map<SlotSoundCueId, object>>(new Map())
  const decodedLoopsRef = useRef<Map<string, Promise<AudioBuffer>>>(new Map())
  const audioContextRef = useRef<AudioContext | null>(null)

  const stopGaplessLoop = useCallback((cueId: SlotSoundCueId) => {
    pendingGaplessLoopsRef.current.delete(cueId)
    const loop = gaplessLoopsRef.current.get(cueId)
    if (loop === undefined) return
    loop.source.stop()
    loop.source.disconnect()
    loop.gain.disconnect()
    gaplessLoopsRef.current.delete(cueId)
  }, [])

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

    gaplessLoopsRef.current.forEach((loop, cueId) => {
      const cue = soundSet.cues[cueId]
      if (!isCueAllowed(cue)) {
        stopGaplessLoop(cueId)
        return
      }
      loop.gain.gain.value = cue.baseVolume * volumeScale
    })

    saveAudioPreferences(preferences)
  }, [isCueAllowed, preferences, soundSet, stopGaplessLoop])

  useEffect(() => () => {
    oneShotsRef.current.forEach((_, audio) => stopAudio(audio))
    loopsRef.current.forEach((audio) => stopAudio(audio))
    for (const cueId of pendingGaplessLoopsRef.current.keys()) stopGaplessLoop(cueId)
    for (const cueId of gaplessLoopsRef.current.keys()) stopGaplessLoop(cueId)
    decodedLoopsRef.current.clear()
    if (audioContextRef.current !== null) void audioContextRef.current.close()
    audioContextRef.current = null
    oneShotsRef.current.clear()
    loopsRef.current.clear()
  }, [soundSet, stopGaplessLoop])

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
    if (audio !== undefined) {
      stopAudio(audio)
      loopsRef.current.delete(cueId)
    }
    stopGaplessLoop(cueId)
  }, [stopGaplessLoop])

  const startLoop = useCallback((cueId: SlotSoundCueId) => {
    const cue = soundSet.cues[cueId]
    if (!isCueAllowed(cue)) {
      return
    }

    if (soundSet.events.themeSuite && (cueId === soundSet.events.ambience || cueId === soundSet.events.specialAmbience)) {
      const musicIds = [soundSet.events.ambience, soundSet.events.specialAmbience]
      musicIds.forEach((otherId) => {
        if (otherId !== undefined && otherId !== cueId) stopLoop(otherId)
      })
    }

    const startMediaLoop = () => {
      const existingAudio = loopsRef.current.get(cueId)
      const audio = existingAudio ?? new Audio(cue.source)
      audio.loop = cue.loop ?? true
      audio.volume = cue.baseVolume * preferencesRef.current.volume / 100
      if (existingAudio === undefined) loopsRef.current.set(cueId, audio)
      void audio.play().catch(() => {
        if (loopsRef.current.get(cueId) === audio && audio.paused) {
          loopsRef.current.delete(cueId)
        }
      })
    }

    if (loopsRef.current.has(cueId)) {
      startMediaLoop()
      return
    }

    if (cue.gaplessLoop && typeof window.AudioContext === 'function') {
      try {
        const context = audioContextRef.current ?? new AudioContext()
        audioContextRef.current = context
        void context.resume().catch(() => { /* A later user gesture retries. */ })
        if (gaplessLoopsRef.current.has(cueId) || pendingGaplessLoopsRef.current.has(cueId)) return

        const request = {}
        pendingGaplessLoopsRef.current.set(cueId, request)
        let decoded = decodedLoopsRef.current.get(cue.source)
        if (decoded === undefined) {
          decoded = fetch(cue.source)
            .then((response) => {
              if (!response.ok) throw new Error(`Audio request failed: ${response.status}`)
              return response.arrayBuffer()
            })
            .then((data) => context.decodeAudioData(data))
          decodedLoopsRef.current.set(cue.source, decoded)
        }
        void decoded.then((buffer) => {
          if (pendingGaplessLoopsRef.current.get(cueId) !== request || !isCueAllowed(cue)) return
          const source = context.createBufferSource()
          const gain = context.createGain()
          source.buffer = buffer
          source.loop = true
          if (cue.loopDurationSeconds !== undefined) {
            source.loopEnd = Math.min(buffer.duration, cue.loopDurationSeconds)
          }
          gain.gain.value = cue.baseVolume * preferencesRef.current.volume / 100
          source.connect(gain).connect(context.destination)
          const startedAt = context.currentTime
          source.start(startedAt)
          gaplessLoopsRef.current.set(cueId, { source, gain, startedAt, context })
        }).catch(() => {
          if (pendingGaplessLoopsRef.current.get(cueId) !== request) return
          decodedLoopsRef.current.delete(cue.source)
          startMediaLoop()
        }).finally(() => {
          if (pendingGaplessLoopsRef.current.get(cueId) === request) {
            pendingGaplessLoopsRef.current.delete(cueId)
          }
        })
        return
      } catch {
        // Older browsers can still play the standard media loop.
      }
    }

    startMediaLoop()
  }, [isCueAllowed, soundSet, stopLoop])

  const getLoopPlaybackSeconds = useCallback((cueId: SlotSoundCueId): number | null => {
    const loop = gaplessLoopsRef.current.get(cueId)
    if (loop !== undefined && loop.context.state === 'running') {
      const output = typeof loop.context.getOutputTimestamp === 'function'
        ? loop.context.getOutputTimestamp()
        : null
      const heardAt = output?.performanceTime !== undefined && output.contextTime !== undefined &&
        output.performanceTime > 0
        ? output.contextTime + (performance.now() - output.performanceTime) / 1000
        : loop.context.currentTime - loop.context.baseLatency
      return Math.max(0, heardAt - loop.startedAt)
    }
    const audio = loopsRef.current.get(cueId)
    return audio !== undefined && !audio.paused ? audio.currentTime : null
  }, [])

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
    getLoopPlaybackSeconds,
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

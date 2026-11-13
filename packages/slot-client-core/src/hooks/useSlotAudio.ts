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

type GaplessLoop = {
  source: AudioBufferSourceNode
  gain: GainNode
  fadeGain?: GainNode
  startedAt: number
  context: AudioContext
  anchorCueId?: SlotSoundCueId
  phaseStartedAt?: number
}

type LoopSync = { anchorCueId: SlotSoundCueId; intervalSeconds: number }

type PendingGaplessLoop = {
  anchorCueId?: SlotSoundCueId
  startWhenAnchorReady?: () => void
}

const syncLookaheadSeconds = .01

// Owns browser Audio objects and user preferences so SlotsPage only requests
// semantic cues from the selected sound set.
export function useSlotAudio(soundSet: SlotSoundSet) {
  const [preferences, setPreferences] = useState(loadAudioPreferences)
  const preferencesRef = useRef(preferences)
  const oneShotsRef = useRef<Map<HTMLAudioElement, SlotSoundCue>>(new Map())
  const loopsRef = useRef<Map<SlotSoundCueId, HTMLAudioElement>>(new Map())
  const gaplessLoopsRef = useRef<Map<SlotSoundCueId, GaplessLoop>>(new Map())
  const pendingGaplessLoopsRef = useRef<Map<SlotSoundCueId, PendingGaplessLoop>>(new Map())
  const decodedLoopsRef = useRef<Map<string, Promise<AudioBuffer>>>(new Map())
  const audioContextRef = useRef<AudioContext | null>(null)

  const stopGaplessLoop = useCallback((cueId: SlotSoundCueId) => {
    const stopped = new Set([cueId])
    for (const id of stopped) {
      pendingGaplessLoopsRef.current.delete(id)
      for (const [dependent, pending] of pendingGaplessLoopsRef.current) {
        if (pending.anchorCueId === id) stopped.add(dependent)
      }
      const loop = gaplessLoopsRef.current.get(id)
      if (loop !== undefined) {
        loop.source.stop()
        loop.source.disconnect()
        loop.fadeGain?.disconnect()
        loop.gain.disconnect()
        gaplessLoopsRef.current.delete(id)
      }
      for (const [dependent, active] of gaplessLoopsRef.current) {
        if (active.anchorCueId === id) stopped.add(dependent)
      }
    }
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

    pendingGaplessLoopsRef.current.forEach((pending, cueId) => {
      if (!isCueAllowed(soundSet.cues[cueId]) ||
          pending.anchorCueId !== undefined && !isCueAllowed(soundSet.cues[pending.anchorCueId])) {
        stopGaplessLoop(cueId)
      }
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

  const getAudioContext = useCallback(() => {
    const context = audioContextRef.current ?? new AudioContext()
    audioContextRef.current = context
    return context
  }, [])

  const getDecodedLoop = useCallback((source: string, context: AudioContext) => {
    const cached = decodedLoopsRef.current.get(source)
    if (cached !== undefined) return cached

    const decoded = fetch(source)
      .then((response) => {
        if (!response.ok) throw new Error(`Audio request failed: ${response.status}`)
        return response.arrayBuffer()
      })
      .then((data) => context.decodeAudioData(data))
    decodedLoopsRef.current.set(source, decoded)
    void decoded.catch(() => {
      if (decodedLoopsRef.current.get(source) === decoded) decodedLoopsRef.current.delete(source)
    })
    return decoded
  }, [])

  const preloadLoop = useCallback((cueId: SlotSoundCueId) => {
    const cue = soundSet.cues[cueId]
    if (!cue?.gaplessLoop || cue.enabled === false || typeof window.AudioContext !== 'function') return
    try {
      void getDecodedLoop(cue.source, getAudioContext())
    } catch {
      // Playback can still use the native media fallback.
    }
  }, [getAudioContext, getDecodedLoop, soundSet])

  const startPlaybackLoop = useCallback((cueId: SlotSoundCueId, fadeInMs: number, sync?: LoopSync) => {
    const cue = soundSet.cues[cueId]
    if (!isCueAllowed(cue)) {
      return
    }

    if (sync !== undefined) {
      const anchorCue = soundSet.cues[sync.anchorCueId]
      if (sync.anchorCueId === cueId || !Number.isFinite(sync.intervalSeconds) || sync.intervalSeconds <= 0 ||
          !cue.gaplessLoop || !isCueAllowed(anchorCue) || !anchorCue.gaplessLoop ||
          typeof window.AudioContext !== 'function' || loopsRef.current.has(sync.anchorCueId)) return
    } else if (soundSet.events.themeSuite && (cueId === soundSet.events.ambience || cueId === soundSet.events.specialAmbience)) {
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
      if (sync === undefined) startMediaLoop()
      return
    }

    if (cue.gaplessLoop && typeof window.AudioContext === 'function') {
      try {
        const context = getAudioContext()
        void context.resume().catch(() => { /* A later user gesture retries. */ })
        if (gaplessLoopsRef.current.has(cueId) || pendingGaplessLoopsRef.current.has(cueId)) return

        const decoded = getDecodedLoop(cue.source, context)
        const request: PendingGaplessLoop = { anchorCueId: sync?.anchorCueId }
        pendingGaplessLoopsRef.current.set(cueId, request)
        const fail = () => {
          if (pendingGaplessLoopsRef.current.get(cueId) !== request) return
          stopGaplessLoop(cueId)
          if (sync === undefined && isCueAllowed(cue)) startMediaLoop()
        }
        const startDecoded = (buffer: AudioBuffer) => {
          if (pendingGaplessLoopsRef.current.get(cueId) !== request) return
          if (!isCueAllowed(cue) || sync !== undefined && !isCueAllowed(soundSet.cues[sync.anchorCueId])) {
            stopGaplessLoop(cueId)
            return
          }

          const anchor = sync === undefined ? undefined : gaplessLoopsRef.current.get(sync.anchorCueId)
          if (sync !== undefined && anchor === undefined) {
            request.startWhenAnchorReady = () => startDecoded(buffer)
            return
          }

          try {
            const source = context.createBufferSource()
            const gain = context.createGain()
            const duration = Math.min(buffer.duration, cue.loopDurationSeconds ?? buffer.duration)
            source.buffer = buffer
            source.loop = true
            if (cue.loopDurationSeconds !== undefined) source.loopEnd = duration
            gain.gain.value = cue.baseVolume * preferencesRef.current.volume / 100

            let startedAt = context.currentTime
            let offset = 0
            let phaseStartedAt: number | undefined
            if (sync !== undefined && anchor !== undefined) {
              phaseStartedAt = anchor.phaseStartedAt ?? anchor.startedAt
              // Leave room for Web Audio to receive the scheduled start before the boundary.
              const elapsed = Math.max(startedAt + syncLookaheadSeconds, anchor.startedAt) - phaseStartedAt
              const nextInterval = Math.floor(elapsed / sync.intervalSeconds) + 1
              startedAt = phaseStartedAt + nextInterval * sync.intervalSeconds
              offset = nextInterval * sync.intervalSeconds % duration
            }

            let fadeGain: GainNode | undefined
            if (Number.isFinite(fadeInMs) && fadeInMs > 0) {
              fadeGain = context.createGain()
              fadeGain.gain.setValueAtTime(0, startedAt)
              fadeGain.gain.linearRampToValueAtTime(1, startedAt + fadeInMs / 1000)
              source.connect(fadeGain).connect(gain)
            } else {
              source.connect(gain)
            }
            gain.connect(context.destination)
            if (sync === undefined) source.start(startedAt)
            else source.start(startedAt, offset)
            gaplessLoopsRef.current.set(cueId, { source, gain, fadeGain, startedAt, context,
              anchorCueId: sync?.anchorCueId, phaseStartedAt })
            pendingGaplessLoopsRef.current.delete(cueId)
          } catch {
            fail()
            return
          }

          for (const waiting of pendingGaplessLoopsRef.current.values()) {
            if (waiting.anchorCueId === cueId) waiting.startWhenAnchorReady?.()
          }
        }
        void decoded.then(startDecoded, fail)
        return
      } catch {
        // Older browsers can still play the standard media loop.
        stopGaplessLoop(cueId)
      }
    }

    if (sync === undefined) startMediaLoop()
  }, [getAudioContext, getDecodedLoop, isCueAllowed, soundSet, stopGaplessLoop, stopLoop])

  const startLoop = useCallback((cueId: SlotSoundCueId, fadeInMs = 0) => {
    startPlaybackLoop(cueId, fadeInMs)
  }, [startPlaybackLoop])

  const startSyncedLoop = useCallback((cueId: SlotSoundCueId, anchorCueId: SlotSoundCueId,
    intervalSeconds: number, fadeInMs = 0) => {
    startPlaybackLoop(cueId, fadeInMs, { anchorCueId, intervalSeconds })
  }, [startPlaybackLoop])

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
      return heardAt < loop.startedAt ? 0 : heardAt - (loop.phaseStartedAt ?? loop.startedAt)
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
    preloadLoop,
    setVolume,
    startLoop,
    startSyncedLoop,
    stopLoop,
    stopResultCues,
    toggleMuted,
    toggleResultsOnly,
  }
}

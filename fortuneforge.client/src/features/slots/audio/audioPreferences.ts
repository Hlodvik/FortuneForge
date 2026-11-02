export type AudioMode = 'all' | 'muted' | 'results-only'
export type AudioPreferences = {
  mode: AudioMode
  volume: number
}

// Version the preference key so browsers that inherited the old muted-by-default
// setting get the corrected sound-on default once. Choices made in this version
// continue to persist normally.
const AUDIO_PREFERENCES_KEY = 'fortune-forge.audio-preferences.v2'
const LEGACY_AUDIO_PREFERENCES_KEY = 'fortune-forge.audio-preferences'
const DEFAULT_AUDIO_PREFERENCES: AudioPreferences = { mode: 'all', volume: 65 }

export function loadAudioPreferences(): AudioPreferences {
  try {
    const savedV2 = window.localStorage.getItem(AUDIO_PREFERENCES_KEY)
    const savedPreferences = JSON.parse(
      savedV2 ?? window.localStorage.getItem(LEGACY_AUDIO_PREFERENCES_KEY) ?? 'null',
    ) as Partial<AudioPreferences> | null
    const savedMode = savedPreferences?.mode
    const savedVolume = savedPreferences?.volume

    return {
      mode: savedV2 !== null && (savedMode === 'muted' || savedMode === 'results-only')
        ? savedMode
        : 'all',
      volume: typeof savedVolume === 'number'
        ? Math.min(100, Math.max(0, Math.round(savedVolume)))
        : DEFAULT_AUDIO_PREFERENCES.volume,
    }
  } catch {
    return DEFAULT_AUDIO_PREFERENCES
  }
}

export function saveAudioPreferences(preferences: AudioPreferences): void {
  try {
    window.localStorage.setItem(AUDIO_PREFERENCES_KEY, JSON.stringify(preferences))
  } catch {
    // Audio remains usable when private browsing or storage policy blocks persistence.
  }
}

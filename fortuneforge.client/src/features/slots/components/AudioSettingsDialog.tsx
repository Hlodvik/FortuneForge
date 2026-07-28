import { useEffect, useRef } from 'react'
import type { AudioPreferences } from '../hooks/useSlotAudio'

type AudioSettingsDialogProps = {
  isOpen: boolean
  onClose: () => void
  onToggleMuted: () => void
  onToggleResultsOnly: () => void
  onVolumeChange: (volume: number) => void
  preferences: AudioPreferences
}

export function AudioSettingsDialog({
  isOpen,
  onClose,
  onToggleMuted,
  onToggleResultsOnly,
  onVolumeChange,
  preferences,
}: AudioSettingsDialogProps) {
  const closeButtonRef = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    if (!isOpen) {
      return undefined
    }

    closeButtonRef.current?.focus()
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [isOpen, onClose])

  if (!isOpen) {
    return null
  }

  return (
    <div
      className="game-settings__backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <section
        className="game-settings"
        role="dialog"
        aria-modal="true"
        aria-labelledby="game-settings-title"
      >
        <button
          ref={closeButtonRef}
          className="game-settings__close"
          type="button"
          aria-label="Close settings"
          onClick={onClose}
        >
          ×
        </button>

        <p className="game-settings__eyebrow">Game preferences</p>
        <h2 id="game-settings-title">Settings</h2>

        <div className="game-settings__tabs" role="tablist" aria-label="Settings sections">
          <button
            id="audio-settings-tab"
            type="button"
            role="tab"
            aria-selected="true"
            aria-controls="audio-settings-panel"
          >
            <span aria-hidden="true">♪</span>
            Audio
          </button>
        </div>

        <div
          id="audio-settings-panel"
          className="audio-settings"
          role="tabpanel"
          aria-labelledby="audio-settings-tab"
        >
          <div className="audio-settings__volume-heading">
            <label htmlFor="game-volume">Volume</label>
            <output htmlFor="game-volume">{preferences.volume}%</output>
          </div>
          <input
            id="game-volume"
            className="audio-settings__slider"
            type="range"
            min="0"
            max="100"
            step="1"
            value={preferences.volume}
            aria-valuetext={`${preferences.volume} percent`}
            onChange={(event) => onVolumeChange(Number(event.target.value))}
          />
          <div className="audio-settings__scale" aria-hidden="true">
            <span>Quiet</span>
            <span>Loud</span>
          </div>

          <div className="audio-settings__modes" aria-label="Sound mode">
            <button
              className={preferences.mode === 'muted'
                ? 'audio-settings__mode audio-settings__mode--active'
                : 'audio-settings__mode'}
              type="button"
              aria-pressed={preferences.mode === 'muted'}
              onClick={onToggleMuted}
            >
              <span className="audio-settings__mode-icon" aria-hidden="true">×</span>
              <span>
                <strong>Mute</strong>
                <small>Turn off all game audio.</small>
              </span>
              <span className="audio-settings__mode-check" aria-hidden="true">✓</span>
            </button>
            <button
              className={preferences.mode === 'results-only'
                ? 'audio-settings__mode audio-settings__mode--active'
                : 'audio-settings__mode'}
              type="button"
              aria-pressed={preferences.mode === 'results-only'}
              onClick={onToggleResultsOnly}
            >
              <span className="audio-settings__mode-icon" aria-hidden="true">★</span>
              <span>
                <strong>Mute all but result sounds</strong>
                <small>Keep wins and outcome cues; mute spins and controls.</small>
              </span>
              <span className="audio-settings__mode-check" aria-hidden="true">✓</span>
            </button>
          </div>
          <p className="audio-settings__note">Audio preferences are saved on this device.</p>
        </div>
      </section>
    </div>
  )
}

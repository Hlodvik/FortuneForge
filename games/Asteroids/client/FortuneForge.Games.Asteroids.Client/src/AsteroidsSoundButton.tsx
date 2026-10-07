import { useState } from 'react'
import { isAsteroidsAudioMuted, setAsteroidsAudioMuted } from './asteroidsAudio'

export function AsteroidsSoundButton({ className }: Readonly<{ className: string }>) {
  const [muted, setMuted] = useState(isAsteroidsAudioMuted)
  const toggle = () => {
    const next = !muted
    setAsteroidsAudioMuted(next)
    setMuted(next)
  }
  return <button
    className={className}
    type="button"
    aria-label={muted ? 'Turn on Asteroid Blaster sound' : 'Mute Asteroid Blaster sound'}
    aria-pressed={muted}
    title={muted ? 'Sound off' : 'Sound on'}
    onClick={toggle}>
    <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M11 5 6 9H3v6h3l5 4Z" />
      {muted ? <path d="m16 9 6 6m0-6-6 6" /> : <><path d="M15 8a6 6 0 0 1 0 8" /><path d="M18 5a10 10 0 0 1 0 14" /></>}
    </svg>
  </button>
}

import shuffle from './assets/audio/card-shuffle.ogg'
import deal from './assets/audio/card-place-1.ogg'
import move from './assets/audio/card-slide-1.ogg'
import chip from './assets/audio/chip-lay-1.ogg'
import collect from './assets/audio/chips-collide-1.ogg'
import click from './assets/audio/chips-handle-1.ogg'

const cues = { shuffle, deal, move, chip, collect, click }
export type HiLoAudioCue = keyof typeof cues
const muteKey = 'fortuneforge:hi-lo:muted'
const activeClips = new Map<HTMLAudioElement, () => void>()

export function readHiLoMuted(): boolean {
  try { return typeof localStorage !== 'undefined' && localStorage.getItem(muteKey) === 'true' }
  catch { return false }
}

export function writeHiLoMuted(muted: boolean): void {
  if (muted) {
    for (const [audio, cleanup] of activeClips) {
      try { audio.pause() }
      catch { /* A media failure must not prevent stopping the remaining clips. */ }
      cleanup()
    }
  }
  try { if (typeof localStorage !== 'undefined') localStorage.setItem(muteKey, String(muted)) }
  catch { /* Audio preference remains effective in memory when storage is denied. */ }
}

/** Invoke from a user interaction; media policy failures never interrupt play. */
export function playHiLoAudio(cue: HiLoAudioCue, muted: boolean): void {
  if (muted || typeof Audio === 'undefined') return
  let cleanup: (() => void) | undefined
  try {
    const audio = new Audio(cues[cue])
    audio.volume = .28
    cleanup = () => {
      activeClips.delete(audio)
      audio.removeEventListener('ended', finished)
      audio.removeEventListener('error', finished)
    }
    const finished = cleanup
    audio.addEventListener('ended', finished, { once: true })
    audio.addEventListener('error', finished, { once: true })
    activeClips.set(audio, finished)
    void audio.play()?.catch(finished)
  } catch { cleanup?.() /* Audio is optional when unavailable or browser policy blocks it. */ }
}

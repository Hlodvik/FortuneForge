import chipSound from './assets/chip-lay-1.ogg?no-inline'
import spinSound from './assets/roulette-spin.wav?no-inline'
import winSound from './assets/chips-handle-1.ogg?no-inline'

const muteStorageKey = 'fortuneforge:roulette:muted'
const active = new Set<HTMLAudioElement>()
let muted = readStoredMute()
let gestureActivated = false

export function getRouletteMuted(): boolean { return muted }

export function setRouletteMuted(next: boolean): void {
  muted = next
  try { window.localStorage.setItem(muteStorageKey, String(next)) } catch { /* Storage can be disabled. */ }
  if (next) stopRouletteAudio()
}

export function playRouletteChip(): void {
  gestureActivated = true
  play(chipSound, .25)
}

export function playRouletteSpin(): void {
  gestureActivated = true
  play(spinSound, .36)
}

export function playRouletteWin(): void {
  // Hydrating an already settled round should never trigger an unsolicited win cue.
  if (!gestureActivated) return
  play(winSound, .32)
}

export function stopRouletteAudio(): void {
  for (const audio of active) release(audio, true)
  gestureActivated = false
}

function readStoredMute(): boolean {
  try { return window.localStorage.getItem(muteStorageKey) === 'true' } catch { return false }
}

function play(source: string, volume: number): void {
  if (muted || typeof Audio === 'undefined') return
  try {
    if (active.size >= 5) release(active.values().next().value!, true)
    const audio = new Audio(source)
    audio.volume = volume
    active.add(audio)
    const finish = () => release(audio)
    audio.addEventListener('ended', finish, { once: true })
    audio.addEventListener('error', finish, { once: true })
    void audio.play()?.catch(finish)
  } catch { /* Browser media restrictions must never interrupt a round. */ }
}

function release(audio: HTMLAudioElement, stop = false): void {
  active.delete(audio)
  if (!stop) return
  try { audio.pause(); audio.currentTime = 0 } catch { /* The media may not have loaded. */ }
}

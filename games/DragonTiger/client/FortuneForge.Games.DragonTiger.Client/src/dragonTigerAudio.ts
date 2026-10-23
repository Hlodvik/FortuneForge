import chip from './assets/chip-lay-1.ogg'
import deal from './assets/card-place-1.ogg'
import result from './assets/chips-collide-1.ogg'
import payout from './assets/chips-handle-1.ogg'

const cues = { click: chip, chip, deal, result, win: payout }
let activeClick: HTMLAudioElement | null = null
let activeWin: HTMLAudioElement | null = null
let gestureActivated = false

export function playDragonTigerAudio(cue: keyof typeof cues, muted: boolean) {
  if (muted) { stopClick(); stopWin(); gestureActivated = false; return }
  if (typeof Audio === 'undefined') return
  if (cue === 'click' || cue === 'chip') gestureActivated = true
  // A restored result cannot start a payout sound before any table interaction.
  if (cue === 'win' && !gestureActivated) return
  try {
    if (cue === 'click') stopClick()
    if (cue === 'win') stopWin()
    const audio = new Audio(cues[cue])
    audio.volume = cue === 'click' ? .16 : cue === 'win' ? .28 : .3
    if (cue === 'click') {
      // A short, dry UI tick, not a growing chorus when a control is tapped rapidly.
      audio.playbackRate = 1.7
      activeClick = audio
      audio.onended = audio.onerror = () => releaseClick(audio)
    } else if (cue === 'win') {
      // Real chips being gathered: natural pitch, brief, with no electronic fanfare.
      activeWin = audio
      audio.onended = audio.onerror = () => releaseWin(audio)
    }
    void audio.play()?.catch(() => {
      if (cue === 'click') releaseClick(audio)
      if (cue === 'win') releaseWin(audio)
    })
  } catch {
    if (cue === 'click') stopClick()
    if (cue === 'win') stopWin()
    // Browser media restrictions and unavailable audio are non-fatal.
  }
}

function releaseClick(audio: HTMLAudioElement): void {
  audio.onended = audio.onerror = null
  if (activeClick === audio) activeClick = null
}

function stopClick(): void {
  if (activeClick === null) return
  const audio = activeClick
  releaseClick(audio)
  try { audio.pause(); audio.currentTime = 0 } catch { /* Media may not have loaded yet. */ }
}

function releaseWin(audio: HTMLAudioElement): void {
  audio.onended = audio.onerror = null
  if (activeWin === audio) activeWin = null
}

function stopWin(): void {
  if (activeWin === null) return
  const audio = activeWin
  releaseWin(audio)
  try { audio.pause(); audio.currentTime = 0 } catch { /* Media may not have loaded yet. */ }
}

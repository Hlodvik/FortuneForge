import chip from './assets/chip-lay-1.ogg'
import deal from './assets/card-place-1.ogg'
import win from './assets/palace-pluck-win.wav'

const cues = { click: chip, chip, deal, result: deal, win }
const playing = new Set<HTMLAudioElement>()
let gestureActivated = false
let activeClick: HTMLAudioElement | null = null

export function playAndarBaharAudio(cue: keyof typeof cues, muted: boolean) {
  if (muted || typeof Audio === 'undefined') return
  if (cue === 'click' || cue === 'chip') gestureActivated = true
  // Restoring a completed deal must not unexpectedly start a musical cue.
  if (cue === 'win' && !gestureActivated) return
  try {
    if (cue === 'click' && activeClick) release(activeClick, true)
    // Rapid tapping may overlap briefly, but never accumulates unlimited players.
    if (playing.size >= 6) release(playing.values().next().value!, true)
    const audio = new Audio(cues[cue])
    audio.volume = cue === 'win' ? .32 : cue === 'click' || cue === 'result' ? .13 : .2
    if (cue === 'click') { audio.playbackRate = 1.6; activeClick = audio }
    playing.add(audio)
    const finish = () => release(audio)
    audio.addEventListener('ended', finish, { once: true })
    audio.addEventListener('error', finish, { once: true })
    void audio.play()?.catch(finish)
  } catch { /* Media restrictions must never interfere with wagering or recovery. */ }
}

function release(audio: HTMLAudioElement, stop = false) {
  playing.delete(audio)
  if (activeClick === audio) activeClick = null
  if (stop) {
    try { audio.pause(); audio.currentTime = 0 } catch { /* A media file may not have loaded yet. */ }
  }
}

export function stopAndarBaharAudio() {
  for (const audio of playing) release(audio, true)
  gestureActivated = false
}

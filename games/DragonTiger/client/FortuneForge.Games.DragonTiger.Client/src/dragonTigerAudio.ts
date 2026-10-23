import chip from './assets/chip-lay-1.ogg'
import deal from './assets/card-place-1.ogg'
import result from './assets/chips-collide-1.ogg'

const cues = { chip, deal, result }
export function playDragonTigerAudio(cue: keyof typeof cues, muted: boolean) {
  if (muted || typeof Audio === 'undefined') return
  const audio = new Audio(cues[cue]); audio.volume = .3
  void audio.play()?.catch(() => undefined)
}

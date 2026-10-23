import chip from './assets/chip-lay-1.ogg'
import deal from './assets/card-place-1.ogg'
import result from './assets/chips-collide-1.ogg'

const cues = { chip, deal, result }
const playing = new Set<HTMLAudioElement>()
export function playAndarBaharAudio(cue: keyof typeof cues, muted: boolean) {
  if (muted || typeof Audio === 'undefined') return
  const audio = new Audio(cues[cue]); audio.volume = cue === 'result' ? .28 : .2
  playing.add(audio)
  const finish = () => playing.delete(audio)
  audio.addEventListener('ended', finish, { once: true })
  audio.addEventListener('error', finish, { once: true })
  void audio.play()?.catch(finish)
}
export function stopAndarBaharAudio() {
  playing.forEach(audio => { audio.pause(); audio.currentTime = 0 }); playing.clear()
}

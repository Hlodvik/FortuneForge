import { useCallback, useEffect, useRef, useState, type MouseEventHandler } from 'react'
import cardPlace from '../../../../assets/audio/cards/card-place-1.ogg'
import cardShuffle from '../../../../assets/audio/cards/card-shuffle.ogg'
import cardSlide from '../../../../assets/audio/cards/card-slide-1.ogg'
import chipLay from '../../../../assets/audio/cards/chip-lay-1.ogg'
import chipsCollide from '../../../../assets/audio/cards/chips-collide-1.ogg'
import chipsHandle from '../../../../assets/audio/cards/chips-handle-1.ogg'

export const cardAudioCues = ['shuffle', 'deal', 'move', 'chip', 'collect', 'click'] as const
export type CardAudioCue = (typeof cardAudioCues)[number]

const sources: Record<CardAudioCue, string> = {
  shuffle: cardShuffle,
  deal: cardPlace,
  move: cardSlide,
  chip: chipLay,
  collect: chipsCollide,
  click: chipsHandle,
}

export function useCardAudioClick(): MouseEventHandler<HTMLElement> {
  return useCallback((event) => {
    if (!(event.target instanceof Element)) return
    const control = event.target.closest<HTMLElement>('button, [role="button"]')
    if (!control || control.matches(':disabled') || control.getAttribute('aria-disabled') === 'true') return

    const cue = control.dataset.cardAudio
    const audio = new Audio(sources[isCardAudioCue(cue) ? cue : 'click'])
    audio.preload = 'auto'
    audio.volume = 0.32
    const playback = audio.play()
    if (playback) void playback.catch(() => undefined)
  }, [])
}

const isCardAudioCue = (value: string | undefined): value is CardAudioCue =>
  cardAudioCues.some((cue) => cue === value)

export function useHeartsAudio() {
  const [muted, setMuted] = useState(() => { try { return localStorage.getItem('fortuneforge:hearts:muted') === 'true' } catch { return false } })
  const muteRef = useRef(muted)
  const playing = useRef(new Set<HTMLAudioElement>())
  const context = useRef<AudioContext | null>(null)
  muteRef.current = muted
  const stop = useCallback(() => { playing.current.forEach(audio => audio.pause()); playing.current.clear() }, [])
  useEffect(() => {
    const hidden = () => { if (document.hidden) stop() }
    document.addEventListener('visibilitychange', hidden)
    return () => { document.removeEventListener('visibilitychange', hidden); stop(); void context.current?.close() }
  }, [stop])
  const play = useCallback((cue: CardAudioCue | 'phase' | 'win') => {
    if (muteRef.current || document.hidden) return
    if (cue === 'phase' || cue === 'win') {
      if (typeof AudioContext === 'undefined') return
      const audio = context.current ??= new AudioContext()
      void audio.resume().catch(() => undefined)
      const notes = cue === 'win' ? [523.25, 659.25, 783.99, 1046.5] : [440, 554.37]
      notes.forEach((frequency, index) => {
        const oscillator = audio.createOscillator(), gain = audio.createGain()
        const start = audio.currentTime + index * .095
        oscillator.type = 'sine'; oscillator.frequency.value = frequency
        gain.gain.setValueAtTime(0, start); gain.gain.linearRampToValueAtTime(.045, start + .015); gain.gain.exponentialRampToValueAtTime(.001, start + .32)
        oscillator.connect(gain); gain.connect(audio.destination); oscillator.start(start); oscillator.stop(start + .35)
      })
      return
    }
    if (typeof Audio === 'undefined') return
    const audio = new Audio(sources[cue]); audio.volume = .28
    playing.current.add(audio)
    audio.addEventListener('ended', () => playing.current.delete(audio), { once: true })
    const playback = audio.play()
    if (playback) void playback.catch(() => playing.current.delete(audio))
  }, [])
  const toggleMute = useCallback(() => { setMuted(current => { const next = !current; muteRef.current = next; if (next) stop(); try { localStorage.setItem('fortuneforge:hearts:muted', String(next)) } catch { /* Preference storage is optional. */ } return next }) }, [stop])
  return { muted, toggleMute, play }
}

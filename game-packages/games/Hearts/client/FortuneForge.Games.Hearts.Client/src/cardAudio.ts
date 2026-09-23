import { useCallback, type MouseEventHandler } from 'react'
import cardPlace from '../../../../../assets/audio/cards/card-place-1.ogg'
import cardShuffle from '../../../../../assets/audio/cards/card-shuffle.ogg'
import cardSlide from '../../../../../assets/audio/cards/card-slide-1.ogg'
import chipLay from '../../../../../assets/audio/cards/chip-lay-1.ogg'
import chipsCollide from '../../../../../assets/audio/cards/chips-collide-1.ogg'
import chipsHandle from '../../../../../assets/audio/cards/chips-handle-1.ogg'

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

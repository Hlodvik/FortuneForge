import { useCallback, type MouseEventHandler } from 'react'
import cardPlace from '../../../assets/audio/cards/card-place-1.ogg'
import cardShuffle from '../../../assets/audio/cards/card-shuffle.ogg'
import cardSlide from '../../../assets/audio/cards/card-slide-1.ogg'
import chipLay from '../../../assets/audio/cards/chip-lay-1.ogg'
import chipsCollide from '../../../assets/audio/cards/chips-collide-1.ogg'
import chipsHandle from '../../../assets/audio/cards/chips-handle-1.ogg'

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

export function playCardAudio(cue: CardAudioCue) {
  if (typeof Audio === 'undefined') return

  const audio = new Audio(sources[cue])
  audio.preload = 'auto'
  audio.volume = 0.32
  const playback = audio.play()
  if (playback) void playback.catch(() => undefined)
}

/**
 * Adds a quiet, user-gesture-only default cue to a card-game surface. Add
 * data-card-audio to an individual control when it has a stronger semantic cue.
 */
export function useCardAudioClick(): MouseEventHandler<HTMLElement> {
  return useCallback((event) => {
    if (typeof Element === 'undefined' || !(event.target instanceof Element)) return
    const control = event.target.closest<HTMLElement>('button, [role="button"]')
    if (!control || control.matches(':disabled') || control.getAttribute('aria-disabled') === 'true') return

    const cue = control.dataset.cardAudio
    playCardAudio(isCardAudioCue(cue) ? cue : 'click')
  }, [])
}

const isCardAudioCue = (value: string | undefined): value is CardAudioCue =>
  cardAudioCues.some((cue) => cue === value)

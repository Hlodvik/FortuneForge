import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'

import music from './assets/audio/i-knew-a-guy-kevin-macleod.mp3'
import win from './assets/audio/vault-win-noir.wav'
import chip from './assets/audio/chip-lay-1.ogg'
import cardPlace from './assets/audio/card-place-1.ogg'
import cardShuffle from './assets/audio/card-shuffle.ogg'

const cue = (source: string, baseVolume: number, category: SlotSoundCue['category'] = 'effect', loop = false): SlotSoundCue =>
  ({ source, baseVolume, category, loop, enabled: true })

const results = {
  bonus: ['five-jewel'],
  five: ['five-twinkle'],
  'no-win': ['soft-miss'],
  premium: ['premium-win'],
  'single-three': ['low-win'],
} as const

/** Existing Midnight sound-set identity and mix, now owned by the game package. */
export const MIDNIGHT_VAULT_SOUNDS: SlotSoundSet = {
  id: 'dragon-hoard-audio-v1',
  cues: {
    ambience: cue(music, .16, 'effect', true),
    'button-click': cue(chip, .11),
    'lever-pull': cue(chip, .26),
    'reel-spin': cue(cardShuffle, .10, 'effect', true),
    'reel-stop': cue(chip, .16),
    'special-reel-spin': cue(cardShuffle, .18, 'effect', true),
    'special-reel-stop': cue(chip, .30),
    'special-collect': cue(chip, .26),
    'low-win': cue(win, .42, 'result'),
    'premium-win': cue(win, .34, 'result'),
    'five-casino-win': cue(win, .42, 'result'),
    'five-jewel': cue(win, .40, 'result'),
    'five-twinkle': cue(win, .52, 'result'),
    'soft-miss': cue(cardPlace, .08, 'result'),
  },
  events: {
    ambience: 'ambience',
    buttonClick: 'button-click',
    leverPull: 'lever-pull',
    reelSpin: 'reel-spin',
    reelStop: 'reel-stop',
    specialReelSpin: 'special-reel-spin',
    specialReelStop: 'special-reel-stop',
    specialCollect: 'special-collect',
    results,
    specialResults: results,
  },
}

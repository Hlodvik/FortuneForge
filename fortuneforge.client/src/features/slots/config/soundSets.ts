import fiveCasinoWinSource from '../../../assets/slots/audio/five-casino-win.wav'
import fiveJewelSource from '../../../assets/slots/audio/five-jewel.wav'
import fiveTwinkleSource from '../../../assets/slots/audio/five-twinkle.wav'
import leverRegisterSource from '../../../assets/slots/audio/lever-register.wav'
import lowWinSource from '../../../assets/slots/audio/low-win.wav'
import premiumWinSource from '../../../assets/slots/audio/premium-win.wav'
import reelSpinSource from '../../../assets/slots/audio/reel-spin.ogg'
import reelStopSource from '../../../assets/slots/audio/reel-stop.wav'

export type AudioCategory = 'effect' | 'result'
export type SlotSoundCueId =
  | 'lever-pull'
  | 'reel-spin'
  | 'reel-stop'
  | 'low-win'
  | 'premium-win'
  | 'five-casino-win'
  | 'five-jewel'
  | 'five-twinkle'
export type SlotResultSoundEvent = 'bonus' | 'five' | 'no-win' | 'premium' | 'single-three'

export type SlotSoundCue = {
  source: string
  baseVolume: number
  category: AudioCategory
  loop?: boolean
}

export type SlotSoundSet = {
  id: string
  cues: Readonly<Record<SlotSoundCueId, SlotSoundCue>>
  events: {
    leverPull: SlotSoundCueId
    reelSpin: SlotSoundCueId
    reelStop: SlotSoundCueId
    results: Readonly<Record<SlotResultSoundEvent, readonly SlotSoundCueId[]>>
  }
}

// Sound identities and levels remain the backwards-compatible default. Large
// source recordings may be trimmed and compressed for browser delivery.
export const DEFAULT_SLOT_SOUNDS: SlotSoundSet = {
  id: 'fortune-forge-default-audio-v2',
  cues: {
    'lever-pull': { source: leverRegisterSource, baseVolume: 0.34, category: 'effect' },
    'reel-spin': { source: reelSpinSource, baseVolume: 0.14, category: 'effect', loop: false },
    'reel-stop': { source: reelStopSource, baseVolume: 0.22, category: 'effect' },
    'low-win': { source: lowWinSource, baseVolume: 0.34, category: 'result' },
    'premium-win': { source: premiumWinSource, baseVolume: 0.38, category: 'result' },
    'five-casino-win': { source: fiveCasinoWinSource, baseVolume: 0.34, category: 'result' },
    'five-jewel': { source: fiveJewelSource, baseVolume: 0.32, category: 'result' },
    'five-twinkle': { source: fiveTwinkleSource, baseVolume: 0.3, category: 'result' },
  },
  events: {
    leverPull: 'lever-pull',
    reelSpin: 'reel-spin',
    reelStop: 'reel-stop',
    results: {
      bonus: ['premium-win', 'five-twinkle'],
      five: ['five-casino-win', 'five-twinkle', 'five-jewel'],
      'no-win': [],
      premium: ['premium-win'],
      'single-three': ['low-win'],
    },
  },
}

import type { SlotSoundSet } from '@fortuneforge/slot-client-core'
import kenneyChipLaySource from '../../../assets/audio/cards/chip-lay-1.ogg'
import kenneyChipsHandleSource from '../../../assets/audio/cards/chips-handle-1.ogg'
import kenneyChipsCollideSource from '../../../assets/audio/cards/chips-collide-1.ogg'
import kenneyCardPlaceSource from '../../../assets/audio/cards/card-place-1.ogg'
import kenneyCardShuffleSource from '../../../assets/audio/cards/card-shuffle.ogg'

export type {
  AudioCategory,
  SlotResultSoundEvent,
  SlotSoundCue,
  SlotSoundCueId,
  SlotSoundSet,
} from '@fortuneforge/slot-client-core'

// The shared fallback deliberately owns no game-specific score or effects.
// This non-fetching identifier preserves the unresolved default music choice.
const casinoRockSource = 'disabled:opentracks/casino-rock.mp3'

// Semantic aliases keep the generic fallback readable. These Kenney Casino
// Audio clips have retained CC0 provenance in the shared host asset library.
const fiveCasinoWinSource = kenneyChipsCollideSource
const fiveJewelSource = kenneyChipsHandleSource
const fiveTwinkleSource = kenneyChipLaySource
const leverRegisterSource = kenneyChipsHandleSource
const lowWinSource = kenneyChipsHandleSource
const noWinWaterDropSource = kenneyCardPlaceSource
const premiumWinSource = kenneyChipsCollideSource
const reelSpinSource = kenneyCardShuffleSource
const reelStopSource = kenneyChipLaySource

// Metadata-only and retained host experiences use this package-neutral palette.
// Standalone games supply their own sound set from their client package.
export const DEFAULT_SLOT_SOUNDS: SlotSoundSet = {
  id: 'fortune-forge-default-audio-v2',
  cues: {
    ambience: { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true, enabled: false },
    'button-click': { source: kenneyChipLaySource, baseVolume: 0.11, category: 'effect', enabled: false },
    'auto-spin-ambience': { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true, enabled: false },
    'lever-pull': { source: leverRegisterSource, baseVolume: 0.34, category: 'effect' },
    'reel-spin': { source: reelSpinSource, baseVolume: 0.14, category: 'effect', loop: false },
    'reel-stop': { source: reelStopSource, baseVolume: 0.22, category: 'effect' },
    'special-reel-spin': { source: reelSpinSource, baseVolume: 0.18, category: 'effect', loop: true },
    'special-reel-stop': { source: reelStopSource, baseVolume: 0.3, category: 'effect' },
    'special-ambience': { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true, enabled: false },
    'special-collect': { source: fiveTwinkleSource, baseVolume: 0.26, category: 'effect' },
    'special-low-win': { source: fiveJewelSource, baseVolume: 0.4, category: 'result' },
    'special-premium-win': { source: fiveCasinoWinSource, baseVolume: 0.46, category: 'result' },
    'special-five-casino-win': { source: premiumWinSource, baseVolume: 0.48, category: 'result' },
    'special-five-jewel': { source: fiveTwinkleSource, baseVolume: 0.42, category: 'result' },
    'special-five-twinkle': { source: fiveJewelSource, baseVolume: 0.44, category: 'result' },
    'special-soft-miss': { source: noWinWaterDropSource, baseVolume: 0.28, category: 'result' },
    'low-win': { source: lowWinSource, baseVolume: 0.34, category: 'result' },
    'premium-win': { source: premiumWinSource, baseVolume: 0.38, category: 'result' },
    'five-casino-win': { source: fiveCasinoWinSource, baseVolume: 0.34, category: 'result' },
    'five-jewel': { source: fiveJewelSource, baseVolume: 0.32, category: 'result' },
    'five-twinkle': { source: fiveTwinkleSource, baseVolume: 0.3, category: 'result' },
    'soft-miss': { source: noWinWaterDropSource, baseVolume: 0.18, category: 'result' },
  },
  events: {
    ambience: 'ambience',
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

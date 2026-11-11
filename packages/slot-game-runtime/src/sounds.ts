import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'

const disabledCue: SlotSoundCue = {
  source: '',
  baseVolume: 0,
  category: 'effect',
  enabled: false,
}

/**
 * Safe fallback for metadata-only manifests. Shippable games should supply an
 * owned sound set; the fallback deliberately has no host asset dependency.
 */
export const NO_SLOT_SOUNDS: SlotSoundSet = {
  id: 'slot-game-runtime-silent-v1',
  cues: {
    ambience: disabledCue,
    'lever-pull': disabledCue,
    'reel-spin': disabledCue,
    'reel-stop': disabledCue,
  },
  events: {
    ambience: 'ambience',
    leverPull: 'lever-pull',
    reelSpin: 'reel-spin',
    reelStop: 'reel-stop',
    results: {
      bonus: [],
      five: [],
      'no-win': [],
      premium: [],
      'single-three': [],
    },
  },
}

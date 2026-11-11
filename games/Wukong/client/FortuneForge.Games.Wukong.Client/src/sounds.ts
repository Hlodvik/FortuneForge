import celestialChime from './assets/audio/wukong-celestial-chime.wav'
import cloudRush from './assets/audio/wukong-cloud-rush.wav'
import gongWin from './assets/audio/wukong-gong-win.wav'
import softStep from './assets/audio/wukong-soft-step.wav'
import staffWhoosh from './assets/audio/wukong-staff-whoosh.wav'
import templeBell from './assets/audio/wukong-temple-bell.wav'
import woodblock from './assets/audio/wukong-woodblock.wav'
import type { SlotSoundSet } from '@fortuneforge/slot-client-core'
import { NO_SLOT_SOUNDS } from '@fortuneforge/slot-game-runtime'

export const WUKONG_TREASURES_SOUNDS: SlotSoundSet = {
  ...NO_SLOT_SOUNDS,
  id: 'wukong-treasures-audio-v3',
  cues: {
    ...NO_SLOT_SOUNDS.cues,
    ambience: { source: 'disabled:opentracks/asian-dragon.mp3', baseVolume: 0.06, category: 'effect', loop: true, enabled: false },
    'lever-pull': { source: staffWhoosh, baseVolume: 0.24, category: 'effect' },
    'reel-spin': { source: cloudRush, baseVolume: 0.1, category: 'effect', loop: true },
    'reel-stop': { source: woodblock, baseVolume: 0.2, category: 'effect' },
    'low-win': { source: templeBell, baseVolume: 0.23, category: 'result' },
    'premium-win': { source: gongWin, baseVolume: 0.27, category: 'result' },
    'five-casino-win': { source: gongWin, baseVolume: 0.3, category: 'result' },
    'five-jewel': { source: celestialChime, baseVolume: 0.26, category: 'result' },
    'five-twinkle': { source: celestialChime, baseVolume: 0.24, category: 'result' },
    'soft-miss': { source: softStep, baseVolume: 0.1, category: 'result' },
  },
  events: {
    ...NO_SLOT_SOUNDS.events,
    results: {
      bonus: ['five-twinkle', 'five-jewel'],
      five: ['five-jewel', 'five-twinkle'],
      'no-win': ['soft-miss'],
      premium: ['five-twinkle'],
      'single-three': ['low-win'],
    },
  },
}

import celestialChime from '../../../assets/slots/audio/wukong-celestial-chime.wav'
import cloudRush from '../../../assets/slots/audio/wukong-cloud-rush.wav'
import gongWin from '../../../assets/slots/audio/wukong-gong-win.wav'
import softStep from '../../../assets/slots/audio/wukong-soft-step.wav'
import staffWhoosh from '../../../assets/slots/audio/wukong-staff-whoosh.wav'
import templeBell from '../../../assets/slots/audio/wukong-temple-bell.wav'
import woodblock from '../../../assets/slots/audio/wukong-woodblock.wav'
import { DEFAULT_SLOT_SOUNDS, type SlotSoundSet } from '../../../features/slots/config/soundSets'

export const WUKONG_TREASURES_SOUNDS: SlotSoundSet = {
  ...DEFAULT_SLOT_SOUNDS,
  id: 'wukong-treasures-audio-v3',
  cues: {
    ...DEFAULT_SLOT_SOUNDS.cues,
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
    ...DEFAULT_SLOT_SOUNDS.events,
    results: {
      bonus: ['five-twinkle', 'five-jewel'],
      five: ['five-jewel', 'five-twinkle'],
      'no-win': ['soft-miss'],
      premium: ['five-twinkle'],
      'single-three': ['low-win'],
    },
  },
}

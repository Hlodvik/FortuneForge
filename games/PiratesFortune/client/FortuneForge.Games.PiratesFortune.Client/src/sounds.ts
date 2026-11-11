import type { SlotSoundSet } from '@fortuneforge/slot-client-core'

const pirateSource = (file: string) => new URL(`./assets/audio/${file}`, import.meta.url).href
const kenneySource = (file: string) => new URL(`./assets/audio/kenney-casino/${file}`, import.meta.url).href

// Preserve the selected score identities without fetching them. These cues
// remain disabled until the product explicitly opts into background music.
const pirateVoyageSource = 'disabled:opentracks/pirate-voyage.mp3'
const casinoRockSource = 'disabled:opentracks/casino-rock.mp3'

const helmSpinSource = pirateSource('pirate-helm-spin.ogg')
const seagullMissSource = pirateSource('pirate-seagull-miss.wav')
const seagullWinSource = pirateSource('pirate-seagull-win.wav')
const beachWavesSource = pirateSource('pirate-wave-ambience.flac')
const chipLaySource = kenneySource('chip-lay-1.ogg')
const chipsCollideSource = kenneySource('chips-collide-1.ogg')
const chipsHandleSource = kenneySource('chips-handle-1.ogg')
const cardShuffleSource = kenneySource('card-shuffle.ogg')

export const PIRATES_FORTUNE_SOUNDS: SlotSoundSet = {
  id: 'pirates-fortune-audio-v9',
  cues: {
    ambience: { source: pirateVoyageSource, baseVolume: 0.05, category: 'effect', loop: true, enabled: false },
    'button-click': { source: chipLaySource, baseVolume: 0.11, category: 'effect', enabled: false },
    'auto-spin-ambience': { source: pirateVoyageSource, baseVolume: 0.05, category: 'effect', loop: true, enabled: false },
    'lever-pull': { source: helmSpinSource, baseVolume: 0.18, category: 'effect' },
    'reel-spin': { source: helmSpinSource, baseVolume: 0.12, category: 'effect', loop: true },
    'reel-stop': { source: helmSpinSource, baseVolume: 0.1, category: 'effect' },
    'special-reel-spin': { source: cardShuffleSource, baseVolume: 0.28, category: 'effect', loop: true },
    'special-reel-stop': { source: chipLaySource, baseVolume: 0.44, category: 'effect' },
    'special-ambience': { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true, enabled: false },
    'special-collect': { source: chipLaySource, baseVolume: 0.26, category: 'effect' },
    'special-low-win': { source: chipsHandleSource, baseVolume: 0.46, category: 'result' },
    'special-premium-win': { source: chipsCollideSource, baseVolume: 0.52, category: 'result' },
    'special-five-casino-win': { source: chipsCollideSource, baseVolume: 0.54, category: 'result' },
    'special-five-jewel': { source: chipLaySource, baseVolume: 0.48, category: 'result' },
    'special-five-twinkle': { source: chipsHandleSource, baseVolume: 0.5, category: 'result' },
    'special-soft-miss': { source: beachWavesSource, baseVolume: 0.14, category: 'result' },
    'low-win': { source: chipsHandleSource, baseVolume: 0.28, category: 'result' },
    'premium-win': { source: chipsCollideSource, baseVolume: 0.38, category: 'result' },
    'five-casino-win': { source: chipsCollideSource, baseVolume: 0.4, category: 'result' },
    'five-jewel': { source: seagullWinSource, baseVolume: 0.17, category: 'result' },
    'five-twinkle': { source: seagullMissSource, baseVolume: 0.15, category: 'result' },
    'pirate-seagull-win': { source: seagullWinSource, baseVolume: 0.17, category: 'result' },
    'soft-miss': { source: beachWavesSource, baseVolume: 0.14, category: 'result' },
  },
  events: {
    ambience: 'ambience',
    autoSpinAmbience: 'auto-spin-ambience',
    leverPull: 'lever-pull',
    reelSpin: 'reel-spin',
    reelStop: 'reel-stop',
    specialReelSpin: 'special-reel-spin',
    specialReelStop: 'special-reel-stop',
    results: {
      bonus: ['pirate-seagull-win', 'five-twinkle'],
      five: ['pirate-seagull-win', 'five-twinkle'],
      'no-win': ['soft-miss'],
      premium: ['low-win', 'five-twinkle'],
      'single-three': ['low-win'],
    },
    specialResults: {
      bonus: ['special-five-casino-win', 'special-five-jewel'],
      five: ['special-premium-win', 'special-five-twinkle'],
      'no-win': ['special-soft-miss'],
      premium: ['special-premium-win', 'special-five-jewel'],
      'single-three': ['special-low-win'],
    },
  },
}

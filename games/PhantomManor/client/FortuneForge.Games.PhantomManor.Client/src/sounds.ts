import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'

const source = (file: string) => new URL(`./assets/audio/${file}`, import.meta.url).href
const effect = (file: string, baseVolume: number, loop = false): SlotSoundCue => ({
  source: source(file), baseVolume, category: 'effect', loop, enabled: true,
})
const result = (file: string, baseVolume: number): SlotSoundCue => ({
  source: source(file), baseVolume, category: 'result', enabled: true,
})
const results: SlotSoundSet['events']['results'] = {
  bonus: ['bonus-entry'], five: ['five-casino-win'], premium: ['premium-win'],
  'single-three': ['low-win'], 'no-win': ['soft-miss'],
}

export const PHANTOM_MANOR_SOUNDS: SlotSoundSet = {
  id: 'phantom-manor-audio-v1',
  cues: {
    ambience: effect('night-drive.ogg', 0.28, true),
    'special-ambience': effect('midnight-mix.ogg', 0.24, true),
    'button-click': effect('button-click.ogg', 0.58),
    'bet-step': effect('bet-step.ogg', 0.54),
    'lever-pull': effect('spin-launch.ogg', 0.65),
    'reel-spin': effect('reel-roll.ogg', 0.4, true),
    'reel-stop': effect('reel-land.ogg', 0.62),
    'special-reel-spin': effect('bonus-reel-roll.ogg', 0.4, true),
    'special-reel-stop': effect('bonus-reel-land.ogg', 0.6),
    'collect-sync': effect('collect-flamingo.ogg', 0.58),
    'collect-rows': effect('collect-dice.ogg', 0.58),
    'collect-paw': effect('collect-sunset.ogg', 0.58),
    'collect-rand': effect('collect-arcade.ogg', 0.58),
    'low-win': result('win-small.ogg', 0.65),
    'premium-win': result('win-medium.ogg', 0.65),
    'five-casino-win': result('win-big.ogg', 0.7),
    'triple-sevens': result('triple-sevens.ogg', 0.67),
    'soft-miss': result('no-win.ogg', 0.3),
    'bonus-entry': result('bonus-entry.ogg', 0.68),
    'bonus-complete': result('bonus-complete.ogg', 0.66),
  },
  events: {
    themeSuite: true, ambience: 'ambience', specialAmbience: 'special-ambience',
    buttonClick: 'button-click', betStep: 'bet-step', leverPull: 'lever-pull',
    reelSpin: 'reel-spin', reelStop: 'reel-stop',
    specialReelSpin: 'special-reel-spin', specialReelStop: 'special-reel-stop',
    collectionArrival: { sync: 'collect-sync', rows: 'collect-rows', paw: 'collect-paw', rand: 'collect-rand' },
    tripleSevens: 'triple-sevens', bonusComplete: 'bonus-complete',
    results, specialResults: results,
  },
}


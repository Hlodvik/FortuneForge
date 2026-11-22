import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'

const source = (file: string) => new URL(`./assets/audio/sunstone-eclipse/${file}`, import.meta.url).href
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

export const SUNSTONE_ECLIPSE_SOUNDS: SlotSoundSet = {
  id: 'sunstone-eclipse-audio-v1',
  cues: {
    ambience: effect('temple-dawn.wav', 0.28, true),
    'special-ambience': effect('eclipse-procession.wav', 0.24, true),
    'button-click': effect('stone-touch.wav', 0.48),
    'bet-step': effect('jade-step.wav', 0.45),
    'lever-pull': effect('wheel-release.wav', 0.62),
    'reel-spin': effect('calendar-turn.wav', 0.32, true),
    'reel-stop': effect('ring-lock.wav', 0.55),
    'special-reel-spin': effect('calendar-turn.wav', 0.38, true),
    'special-reel-stop': effect('ring-lock.wav', 0.62),
    'collect-sync': effect('sun-glint.wav', 0.5),
    'collect-rows': effect('sun-glint.wav', 0.5),
    'collect-paw': effect('guardian-awakens.wav', 0.5),
    'collect-rand': effect('guardian-awakens.wav', 0.5),
    'low-win': result('sun-glint.wav', 0.58),
    'premium-win': result('guardian-awakens.wav', 0.62),
    'five-casino-win': result('fifth-sun.wav', 0.66),
    'triple-sevens': result('guardian-awakens.wav', 0.58),
    'soft-miss': result('temple-breath.wav', 0.22),
    'bonus-entry': result('eclipse-opens.wav', 0.64),
    'bonus-complete': result('sunrise-returns.wav', 0.58),
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

export const ROBOT_REVOLUTION_SOUNDS = SUNSTONE_ECLIPSE_SOUNDS


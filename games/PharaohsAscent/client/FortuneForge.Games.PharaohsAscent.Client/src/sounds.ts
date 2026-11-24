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

export const PHARAOHS_ASCENT_SOUNDS: SlotSoundSet = {
  id: 'pharaohs-ascent-audio-v1',
  cues: {
    ambience: effect('pharaoh-temple-procession.ogg', 0.27, true),
    'special-ambience': effect('pharaoh-guardians-awaken.ogg', 0.25, true),
    'button-click': effect('pharaoh-stone-touch.ogg', 0.5),
    'bet-step': effect('pharaoh-wager-step.ogg', 0.46),
    'lever-pull': effect('pharaoh-gate-release.ogg', 0.59),
    'reel-spin': effect('pharaoh-stone-reels.ogg', 0.35, true),
    'reel-stop': effect('pharaoh-reel-lock.ogg', 0.56),
    'special-reel-spin': effect('pharaoh-guardian-reels.ogg', 0.35, true),
    'special-reel-stop': effect('pharaoh-guardian-lock.ogg', 0.54),
    'collect-sync': effect('pharaoh-seal-horus.ogg', 0.53),
    'collect-rows': effect('pharaoh-seal-thoth.ogg', 0.53),
    'collect-paw': effect('pharaoh-seal-anubis.ogg', 0.53),
    'collect-rand': effect('pharaoh-seal-wadjet.ogg', 0.53),
    'low-win': result('pharaoh-treasure-small.ogg', 0.59),
    'premium-win': result('pharaoh-treasure-great.ogg', 0.61),
    'five-casino-win': result('pharaoh-treasure-royal.ogg', 0.65),
    'triple-sevens': result('pharaoh-scarab-trail.ogg', 0.61),
    'soft-miss': result('pharaoh-temple-rest.ogg', 0.22),
    'bonus-entry': result('pharaoh-chamber-opens.ogg', 0.64),
    'bonus-complete': result('pharaoh-chamber-complete.ogg', 0.61),
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


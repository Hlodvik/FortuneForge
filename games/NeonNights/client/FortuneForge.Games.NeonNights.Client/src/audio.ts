import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'
import baseMusic from './assets/audio/synthwave-pad-110bpm-v1.ogg'
import baseDrums from './assets/audio/base-drums-110bpm-v1.ogg'
import specialMusic from './assets/audio/special-plays-music-v1.ogg'
import discoMusic from './assets/audio/disco-synthwave-105.ogg'
import button from './assets/audio/button-click.ogg'
import bet from './assets/audio/bet-step.ogg'
import launch from './assets/audio/spin-launch.ogg'
import roll from './assets/audio/reel-roll.ogg'
import land from './assets/audio/reel-land.ogg'
import bonusRoll from './assets/audio/bonus-reel-roll.ogg'
import bonusLand from './assets/audio/bonus-reel-land.ogg'
import discoColumnLand from './assets/audio/disco-column-land.ogg'
import nice from './assets/audio/win-nice-v2.ogg'
import niceEmphatic from './assets/audio/win-nice-emphatic-v1.ogg'
import niceOriginal from './assets/audio/win-nice-v1.ogg'
import great from './assets/audio/win-great-v1.ogg'
import perfect from './assets/audio/win-perfect-v1.ogg'
import miss from './assets/audio/no-win.ogg'
import bonusEntry from './assets/audio/bonus-entry.ogg'
import bonusComplete from './assets/audio/bonus-complete.ogg'
import expansion from './assets/audio/arcade-expansion.wav'
import nitroSpool from './assets/audio/nitro-spool-v1.wav'
import collectKoi from './assets/audio/collect-flamingo.ogg'
import collectDice from './assets/audio/collect-dice.ogg'
import collectShades from './assets/audio/collect-sunset.ogg'
import collectArcade from './assets/audio/collect-arcade.ogg'

const cue = (source: string, baseVolume: number, category: SlotSoundCue['category'] = 'effect', loop = false): SlotSoundCue => ({ source, baseVolume, category, loop, enabled: true })
const disabled = cue('', 0)
disabled.enabled = false
const results = { bonus: ['bonus-entry'], five: ['five-casino-win'], premium: ['premium-win'], 'single-three': ['low-win'], 'no-win': ['soft-miss'] } as const

export const neonBaseMusic = {
  cueId: 'ambience',
  drumCueId: 'base-drums',
  barSeconds: 4 * 60 / 110,
  loopSeconds: 769_745 / 44_100,
} as const
export const neonDiscoMusic = { cueId: 'disco-ambience', bpm: 105, loopBeats: 32 } as const
export const neonCarMusicCueId = 'special-ambience'

export type NeonWinTier = 'regular' | 'great' | 'big'
const neonBaseNiceCues = ['low-win', 'nice-emphatic', 'nice-original'] as const

export function getNeonWinTier(amount: number, wager: number): NeonWinTier {
  if (amount >= Math.max(500, wager * 50)) return 'big'
  return amount >= wager * 10 ? 'great' : 'regular'
}

export function getNeonWinReaction(amount: number, wager: number, basePlay = false) {
  if (amount <= 0) return 'soft-miss'
  const tier = getNeonWinTier(amount, wager)
  if (tier === 'big') return 'five-casino-win'
  if (tier === 'great') return 'premium-win'
  if (basePlay) return neonBaseNiceCues[Math.floor(Math.random() * neonBaseNiceCues.length)]
  return amount >= wager * 2 ? 'nice-emphatic' : 'low-win'
}

export const NEON_NIGHTS_SOUNDS: SlotSoundSet = {
  id: 'neon-nights-audio-v1',
  cues: {
    [neonBaseMusic.cueId]: { ...cue(baseMusic, .62, 'effect', true), gaplessLoop: true, loopDurationSeconds: neonBaseMusic.loopSeconds },
    [neonBaseMusic.drumCueId]: { ...cue(baseDrums, .58, 'effect', true), gaplessLoop: true, loopDurationSeconds: neonBaseMusic.loopSeconds },
    [neonCarMusicCueId]: { ...cue(specialMusic, .50, 'effect', true), gaplessLoop: true, loopDurationSeconds: 9.6 },
    [neonDiscoMusic.cueId]: {
      ...cue(discoMusic, .50, 'effect', true),
      gaplessLoop: true,
      loopDurationSeconds: neonDiscoMusic.loopBeats * 60 / neonDiscoMusic.bpm,
    },
    'button-click': cue(button, .58), 'bet-step': cue(bet, .54), 'lever-pull': cue(launch, .65),
    'reel-spin': cue(roll, .40, 'effect', true), 'reel-stop': cue(land, .62),
    'special-reel-spin': cue(bonusRoll, .40, 'effect', true), 'special-reel-stop': cue(bonusLand, .60),
    'disco-column-land': cue(discoColumnLand, .38),
    'nitro-spool': cue(nitroSpool, .34),
    'low-win': cue(nice, .58, 'result'), 'nice-emphatic': cue(niceEmphatic, .58, 'result'),
    'nice-original': cue(niceOriginal, .58, 'result'),
    'premium-win': cue(great, .58, 'result'), 'five-casino-win': cue(perfect, .58, 'result'),
    'triple-sevens': cue(great, .58, 'result'),
    'bonus-entry': cue(bonusEntry, .68, 'result'), 'bonus-complete': cue(bonusComplete, .66, 'result'),
    'soft-miss': cue(miss, .30, 'result'), 'arcade-expansion': cue(expansion, .54),
    'collect-sync': cue(collectKoi, .58), 'collect-rows': cue(collectDice, .58),
    'collect-paw': cue(collectShades, .58), 'collect-rand': cue(collectArcade, .58),
    'auto-spin-ambience': disabled, 'special-collect': disabled, 'special-low-win': disabled,
    'special-premium-win': disabled, 'special-five-casino-win': disabled, 'special-five-jewel': disabled,
    'special-five-twinkle': disabled, 'special-soft-miss': disabled, 'five-jewel': disabled,
    'five-twinkle': disabled, 'pirate-seagull-win': disabled,
  },
  events: {
    themeSuite: true, ambience: neonBaseMusic.cueId, specialAmbience: neonCarMusicCueId,
    buttonClick: 'button-click', betStep: 'bet-step', leverPull: 'lever-pull',
    reelSpin: 'reel-spin', reelStop: 'reel-stop', specialReelSpin: 'special-reel-spin', specialReelStop: 'special-reel-stop',
    collectionArrival: { sync: 'collect-sync', rows: 'collect-rows', paw: 'collect-paw', rand: 'collect-rand' },
    tripleSevens: 'triple-sevens', bonusComplete: 'bonus-complete', arcadeExpansion: 'arcade-expansion',
    results, specialResults: results,
  },
}

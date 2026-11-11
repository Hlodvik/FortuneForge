import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'
import nightDrive from './assets/audio/night-drive.ogg'
import specialMusic from './assets/audio/special-plays-music-v1.ogg'
import button from './assets/audio/button-click.ogg'
import bet from './assets/audio/bet-step.ogg'
import launch from './assets/audio/spin-launch.ogg'
import roll from './assets/audio/reel-roll.ogg'
import land from './assets/audio/reel-land.ogg'
import bonusRoll from './assets/audio/bonus-reel-roll.ogg'
import bonusLand from './assets/audio/bonus-reel-land.ogg'
import discoColumnLand from './assets/audio/disco-column-land.ogg'
import nice from './assets/audio/win-nice-v1.ogg'
import miss from './assets/audio/no-win.ogg'
import bonusEntry from './assets/audio/bonus-entry.ogg'
import bonusComplete from './assets/audio/bonus-complete.ogg'
import expansion from './assets/audio/arcade-expansion.wav'
import collectKoi from './assets/audio/collect-flamingo.ogg'
import collectDice from './assets/audio/collect-dice.ogg'
import collectShades from './assets/audio/collect-sunset.ogg'
import collectArcade from './assets/audio/collect-arcade.ogg'

const cue = (source: string, baseVolume: number, category: SlotSoundCue['category'] = 'effect', loop = false): SlotSoundCue => ({ source, baseVolume, category, loop, enabled: true })
const disabled = cue('', 0)
disabled.enabled = false
const results = { bonus: ['bonus-entry'], five: ['five-casino-win'], premium: ['premium-win'], 'single-three': ['low-win'], 'no-win': ['soft-miss'] } as const
export const NEON_NIGHTS_SOUNDS: SlotSoundSet = {
  id: 'neon-nights-audio-v1',
  cues: {
    ambience: cue(nightDrive, .62, 'effect', true),
    'special-ambience': { ...cue(specialMusic, .50, 'effect', true), gaplessLoop: true, loopDurationSeconds: 9.6 },
    'button-click': cue(button, .58), 'bet-step': cue(bet, .54), 'lever-pull': cue(launch, .65),
    'reel-spin': cue(roll, .40, 'effect', true), 'reel-stop': cue(land, .62),
    'special-reel-spin': cue(bonusRoll, .40, 'effect', true), 'special-reel-stop': cue(bonusLand, .60),
    'disco-column-land': cue(discoColumnLand, .38),
    'low-win': cue(nice, .58, 'result'), 'premium-win': cue(nice, .58, 'result'),
    'five-casino-win': cue(nice, .58, 'result'), 'triple-sevens': cue(nice, .58, 'result'),
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
    themeSuite: true, ambience: 'ambience', specialAmbience: 'special-ambience',
    buttonClick: 'button-click', betStep: 'bet-step', leverPull: 'lever-pull',
    reelSpin: 'reel-spin', reelStop: 'reel-stop', specialReelSpin: 'special-reel-spin', specialReelStop: 'special-reel-stop',
    collectionArrival: { sync: 'collect-sync', rows: 'collect-rows', paw: 'collect-paw', rand: 'collect-rand' },
    tripleSevens: 'triple-sevens', bonusComplete: 'bonus-complete', arcadeExpansion: 'arcade-expansion',
    results, specialResults: results,
  },
}

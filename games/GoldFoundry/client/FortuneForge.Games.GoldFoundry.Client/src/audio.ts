import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'
import ambience from './assets/audio/foundry-working-room.ogg'
import button from './assets/audio/hammer-ring.wav'
import spinButton from './assets/audio/spin-button-metal-hit.wav'
import reelSpin from './assets/audio/forge-bellows-and-crackling-fire.wav'
import reelStop from './assets/audio/reel-brake.wav'
import lowWin from './assets/audio/win-small.ogg'
import premiumWin from './assets/audio/win-medium.ogg'
import bigWin from './assets/audio/win-big.ogg'
import tripleSevens from './assets/audio/triple-sevens.ogg'
import miss from './assets/audio/no-win.ogg'

const cue = (source: string, baseVolume: number, category: SlotSoundCue['category'] = 'effect', loop = false): SlotSoundCue =>
  ({ source, baseVolume, category, loop, enabled: true })

const workshopAmbience = cue(ambience, .6, 'effect', true)

export const GOLD_FOUNDRY_SOUNDS: SlotSoundSet = {
  id: 'gold-foundry-audio-v1',
  cues: {
    ambience: workshopAmbience,
    'special-ambience': workshopAmbience,
    'button-click': cue(button, .88),
    'spin-button-click': cue(spinButton, .88),
    'lever-pull': { ...cue(button, 0), enabled: false },
    'reel-spin': cue(reelSpin, .78, 'effect', true),
    'reel-stop': cue(reelStop, .22),
    'low-win': cue(lowWin, .65, 'result'),
    'premium-win': cue(premiumWin, .65, 'result'),
    'five-casino-win': cue(bigWin, .7, 'result'),
    'triple-sevens': cue(tripleSevens, .67, 'result'),
    'soft-miss': cue(miss, .3, 'result'),
  },
  events: {
    themeSuite: true,
    ambience: 'ambience', specialAmbience: 'special-ambience', buttonClick: 'button-click',
    betStep: 'button-click', spinButtonClick: 'spin-button-click', stopButtonClick: 'button-click',
    leverPull: 'lever-pull', reelSpin: 'reel-spin', reelStop: 'reel-stop',
    specialReelSpin: 'reel-spin', specialReelStop: 'reel-stop', tripleSevens: 'triple-sevens',
    results: { bonus: [], five: ['five-casino-win'], premium: ['premium-win'], 'single-three': ['low-win'], 'no-win': ['soft-miss'] },
  },
}

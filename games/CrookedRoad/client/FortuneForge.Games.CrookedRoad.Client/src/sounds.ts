import type { SlotSoundCue, SlotSoundSet } from '@fortuneforge/slot-client-core'
import harpLoop from './assets/audio/crooked-road-harp-loop-v1.wav'
import click from './assets/audio/crooked-road-click-v1.wav'
import step from './assets/audio/crooked-road-step-v1.wav'
import coin from './assets/audio/crooked-road-coin-v1.wav'
import spinStart from './assets/audio/crooked-road-spin-start-v1.wav'
import reelRoll from './assets/audio/crooked-road-reel-roll-v1.wav'
import reelLand from './assets/audio/crooked-road-reel-land-v1.wav'
import smallWin from './assets/audio/crooked-road-small-win-v1.wav'
import bigWin from './assets/audio/crooked-road-big-win-v1.wav'
import journeyStart from './assets/audio/crooked-road-journey-start-v1.wav'
import journeyEnd from './assets/audio/crooked-road-journey-end-v1.wav'

const effect = (source: string, baseVolume: number): SlotSoundCue => ({
  source, baseVolume, category: 'effect', enabled: true,
})

const result = (source: string, baseVolume: number): SlotSoundCue => ({
  source, baseVolume, category: 'result', enabled: true,
})

const results: SlotSoundSet['events']['results'] = {
  bonus: ['journey-start'],
  five: ['big-win'],
  premium: ['big-win'],
  'single-three': ['coin-tick', 'small-win'],
  'no-win': [],
}

export const CROOKED_ROAD_SOUNDS: SlotSoundSet = {
  id: 'crooked-road-audio-v1',
  cues: {
    ambience: { ...effect(harpLoop, 0.18), loop: true, gaplessLoop: true, loopDurationSeconds: 8 },
    'stone-click': effect(click, 0.45),
    'stone-step': effect(step, 0.45),
    'coin-tick': result(coin, 0.52),
    'spin-start': effect(spinStart, 0.54),
    'reel-roll': { ...effect(reelRoll, 0.30), loop: true, gaplessLoop: true, loopDurationSeconds: 0.96 },
    'reel-land': effect(reelLand, 0.50),
    'small-win': result(smallWin, 0.70),
    'big-win': result(bigWin, 0.75),
    'journey-start': result(journeyStart, 0.65),
    'journey-end': result(journeyEnd, 0.65),
  },
  events: {
    themeSuite: true,
    ambience: 'ambience',
    buttonClick: 'stone-step',
    utilityButtonClick: 'stone-click',
    betStep: 'stone-click',
    spinButtonClick: 'spin-start',
    stopButtonClick: 'stone-click',
    leverPull: 'spin-start',
    reelSpin: 'reel-roll',
    reelStop: 'reel-land',
    specialReelSpin: 'reel-roll',
    specialReelStop: 'reel-land',
    specialCollect: 'stone-step',
    bonusComplete: 'journey-end',
    results,
    specialResults: results,
  },
}

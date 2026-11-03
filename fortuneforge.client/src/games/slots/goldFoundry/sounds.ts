import { DEFAULT_SLOT_SOUNDS, type SlotSoundCue, type SlotSoundSet } from '../../../features/slots/config/soundSets'

const audioPath = '/assets/slots/gold-foundry/v1/audio'
const workshopAmbience: SlotSoundCue = {
  source: `${audioPath}/foundry-working-room.ogg`,
  baseVolume: 0.6,
  category: 'effect',
  loop: true,
  enabled: true,
}

const resultCue = (name: string, baseVolume: number): SlotSoundCue => ({
  source: `${audioPath}/${name}.ogg`, baseVolume, category: 'result', enabled: true,
})

export const GOLD_FOUNDRY_SOUNDS: SlotSoundSet = {
  id: 'gold-foundry-audio-v1',
  cues: {
    ...DEFAULT_SLOT_SOUNDS.cues,
    ambience: workshopAmbience,
    'special-ambience': workshopAmbience,
    'button-click': { source: `${audioPath}/hammer-ring.wav`, baseVolume: 0.88, category: 'effect' },
    'spin-button-click': { source: `${audioPath}/spin-button-metal-hit.wav`, baseVolume: 0.88, category: 'effect' },
    'lever-pull': { ...DEFAULT_SLOT_SOUNDS.cues['lever-pull'], enabled: false },
    'reel-spin': { source: `${audioPath}/forge-bellows-and-crackling-fire.wav`, baseVolume: 0.78, category: 'effect', loop: true },
    'reel-stop': { source: `${audioPath}/reel-brake.wav`, baseVolume: 0.22, category: 'effect' },
    'low-win': resultCue('win-small', 0.65),
    'premium-win': resultCue('win-medium', 0.65),
    'five-casino-win': resultCue('win-big', 0.70),
    'triple-sevens': resultCue('triple-sevens', 0.67),
    'soft-miss': resultCue('no-win', 0.30),
  },
  events: {
    themeSuite: true,
    ambience: 'ambience',
    specialAmbience: 'special-ambience',
    buttonClick: 'button-click',
    betStep: 'button-click',
    spinButtonClick: 'spin-button-click',
    stopButtonClick: 'button-click',
    leverPull: 'lever-pull',
    reelSpin: 'reel-spin',
    reelStop: 'reel-stop',
    specialReelSpin: 'reel-spin',
    specialReelStop: 'reel-stop',
    tripleSevens: 'triple-sevens',
    results: {
      bonus: [],
      five: ['five-casino-win'],
      premium: ['premium-win'],
      'single-three': ['low-win'],
      'no-win': ['soft-miss'],
    },
  },
}

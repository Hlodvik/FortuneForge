import { DEFAULT_SLOT_SOUNDS, type SlotSoundCue, type SlotSoundSet } from '../../../features/slots/config/soundSets'
import { createThemedSlotAudio } from '../../../features/slots/config/themedSlotAudio'

const suite = createThemedSlotAudio('dino-dominion', {
  ...DEFAULT_SLOT_SOUNDS,
  id: 'dino-dominion-audio-v1',
})
const audioPath = '/assets/slots/gold-foundry/v1/audio'
const workshopAmbience: SlotSoundCue = {
  source: `${audioPath}/foundry-working-room.ogg`,
  baseVolume: 0.6,
  category: 'effect',
  loop: true,
  enabled: true,
}

export const GOLD_FOUNDRY_SOUNDS: SlotSoundSet = {
  ...suite,
  cues: {
    ...suite.cues,
    ambience: workshopAmbience,
    'special-ambience': workshopAmbience,
    'button-click': { source: `${audioPath}/hammer-ring.wav`, baseVolume: 0.88, category: 'effect' },
    'spin-button-click': { source: `${audioPath}/spin-button-metal-hit.wav`, baseVolume: 0.88, category: 'effect' },
    'lever-pull': { ...suite.cues['lever-pull'], enabled: false },
    'reel-spin': { source: `${audioPath}/forge-bellows-and-crackling-fire.wav`, baseVolume: 0.78, category: 'effect', loop: true },
    'reel-stop': { source: `${audioPath}/reel-brake.wav`, baseVolume: 0.22, category: 'effect' },
  },
  events: {
    ...suite.events,
    betStep: 'button-click',
    spinButtonClick: 'spin-button-click',
    stopButtonClick: 'button-click',
    specialReelSpin: 'reel-spin',
    specialReelStop: 'reel-stop',
  },
}

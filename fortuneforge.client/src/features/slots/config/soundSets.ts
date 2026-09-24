import fiveCasinoWinSource from '../../../assets/slots/audio/five-casino-win.wav'
import fiveJewelSource from '../../../assets/slots/audio/five-jewel.wav'
import fiveTwinkleSource from '../../../assets/slots/audio/five-twinkle.wav'
import ancientSwordSource from '../../../assets/slots/ambient/ancient-sword.mp3'
import asianDragonSource from '../../../assets/slots/ambient/asian-dragon.mp3'
import carnivalLightsSource from '../../../assets/slots/ambient/carnival-lights.mp3'
import casinoRockSource from '../../../assets/slots/ambient/casino-rock.mp3'
import countryTrailSource from '../../../assets/slots/ambient/country-trail.mp3'
import cyberNightSource from '../../../assets/slots/ambient/cyber-night.mp3'
import desertWindSource from '../../../assets/slots/ambient/desert-wind.mp3'
import forestJourneySource from '../../../assets/slots/ambient/forest-journey.mp3'
import hauntedChimesSource from '../../../assets/slots/ambient/haunted-chimes.mp3'
import nordicAtmosphereSource from '../../../assets/slots/ambient/nordic-atmosphere.mp3'
import pirateVoyageSource from '../../../assets/slots/ambient/pirate-voyage.mp3'
import leverCoinSource from '../../../assets/slots/audio/lever-coin.wav'
import leverRegisterSource from '../../../assets/slots/audio/lever-register.wav'
import lowWinSource from '../../../assets/slots/audio/low-win.wav'
import noWinWaterDropSource from '../../../assets/slots/audio/no-win-water-drop.wav'
import premiumWinSource from '../../../assets/slots/audio/premium-win.wav'
import pirateHelmSpinSource from '../../../assets/slots/audio/pirate-helm-spin.ogg'
import pirateSeagullMissSource from '../../../assets/slots/audio/pirate-seagull-miss.wav'
import pirateSeagullWinSource from '../../../assets/slots/audio/pirate-seagull-win.wav'
import pirateBeachWavesSource from '../../../assets/slots/audio/pirate-wave-ambience.flac'
import reelSpinSource from '../../../assets/slots/audio/reel-spin.ogg'
import reelStopSource from '../../../assets/slots/audio/reel-stop.wav'

export type AudioCategory = 'effect' | 'result'
export type SlotSoundCueId =
  | 'ambience'
  | 'auto-spin-ambience'
  | 'lever-pull'
  | 'reel-spin'
  | 'reel-stop'
  | 'special-reel-spin'
  | 'special-reel-stop'
  | 'special-low-win'
  | 'special-premium-win'
  | 'special-five-casino-win'
  | 'special-five-jewel'
  | 'special-five-twinkle'
  | 'special-soft-miss'
  | 'low-win'
  | 'premium-win'
  | 'five-casino-win'
  | 'five-jewel'
  | 'five-twinkle'
  | 'pirate-seagull-win'
  | 'soft-miss'
export type SlotResultSoundEvent = 'bonus' | 'five' | 'no-win' | 'premium' | 'single-three'

export type SlotSoundCue = {
  source: string
  baseVolume: number
  category: AudioCategory
  loop?: boolean
}

export type SlotSoundSet = {
  id: string
  cues: Readonly<Record<SlotSoundCueId, SlotSoundCue>>
  events: {
    ambience: SlotSoundCueId
    autoSpinAmbience?: SlotSoundCueId
    leverPull: SlotSoundCueId
    reelSpin: SlotSoundCueId
    reelStop: SlotSoundCueId
    specialReelSpin?: SlotSoundCueId
    specialReelStop?: SlotSoundCueId
    results: Readonly<Record<SlotResultSoundEvent, readonly SlotSoundCueId[]>>
    specialResults?: Readonly<Record<SlotResultSoundEvent, readonly SlotSoundCueId[]>>
  }
}

// Sound identities and levels remain the backwards-compatible default. Large
// source recordings may be trimmed and compressed for browser delivery.
export const DEFAULT_SLOT_SOUNDS: SlotSoundSet = {
  id: 'fortune-forge-default-audio-v2',
  cues: {
    ambience: { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true },
    'auto-spin-ambience': { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true },
    'lever-pull': { source: leverRegisterSource, baseVolume: 0.34, category: 'effect' },
    'reel-spin': { source: reelSpinSource, baseVolume: 0.14, category: 'effect', loop: false },
    'reel-stop': { source: reelStopSource, baseVolume: 0.22, category: 'effect' },
    'special-reel-spin': { source: reelSpinSource, baseVolume: 0.18, category: 'effect', loop: true },
    'special-reel-stop': { source: reelStopSource, baseVolume: 0.3, category: 'effect' },
    'special-low-win': { source: fiveJewelSource, baseVolume: 0.4, category: 'result' },
    'special-premium-win': { source: fiveCasinoWinSource, baseVolume: 0.46, category: 'result' },
    'special-five-casino-win': { source: premiumWinSource, baseVolume: 0.48, category: 'result' },
    'special-five-jewel': { source: fiveTwinkleSource, baseVolume: 0.42, category: 'result' },
    'special-five-twinkle': { source: fiveJewelSource, baseVolume: 0.44, category: 'result' },
    'special-soft-miss': { source: noWinWaterDropSource, baseVolume: 0.28, category: 'result' },
    'low-win': { source: lowWinSource, baseVolume: 0.34, category: 'result' },
    'premium-win': { source: premiumWinSource, baseVolume: 0.38, category: 'result' },
    'five-casino-win': { source: fiveCasinoWinSource, baseVolume: 0.34, category: 'result' },
    'five-jewel': { source: fiveJewelSource, baseVolume: 0.32, category: 'result' },
    'five-twinkle': { source: fiveTwinkleSource, baseVolume: 0.3, category: 'result' },
    'pirate-seagull-win': { source: pirateSeagullMissSource, baseVolume: 0.16, category: 'result' },
    'soft-miss': { source: noWinWaterDropSource, baseVolume: 0.18, category: 'result' },
  },
  events: {
    ambience: 'ambience',
    leverPull: 'lever-pull',
    reelSpin: 'reel-spin',
    reelStop: 'reel-stop',
    results: {
      bonus: ['premium-win', 'five-twinkle'],
      five: ['five-casino-win', 'five-twinkle', 'five-jewel'],
      'no-win': [],
      premium: ['premium-win'],
      'single-three': ['low-win'],
    },
  },
}

type ThemedSoundOptions = {
  ambienceSource: string
  ambienceVolume: number
  leverVolume: number
  leverSource: string
  bonus: readonly SlotSoundCueId[]
  five: readonly SlotSoundCueId[]
  premium: readonly SlotSoundCueId[]
}

function createThemedSlotSounds(id: string, options: ThemedSoundOptions): SlotSoundSet {
  return {
    ...DEFAULT_SLOT_SOUNDS,
    id,
    cues: {
      ...DEFAULT_SLOT_SOUNDS.cues,
      ambience: {
        source: options.ambienceSource,
        baseVolume: options.ambienceVolume,
        category: 'effect',
        loop: true,
      },
      'lever-pull': { source: options.leverSource, baseVolume: options.leverVolume, category: 'effect' },
    },
    events: {
      ...DEFAULT_SLOT_SOUNDS.events,
      results: {
        ...DEFAULT_SLOT_SOUNDS.events.results,
        bonus: options.bonus,
        five: options.five,
        premium: options.premium,
        'no-win': ['soft-miss'],
      },
    },
  }
}

export const COSMIC_FORTUNE_SOUNDS = createThemedSlotSounds('cosmic-fortune-audio-v1', {
  ambienceSource: cyberNightSource, ambienceVolume: 0.055,
  leverSource: leverCoinSource, leverVolume: 0.26,
  bonus: ['five-twinkle', 'premium-win'], five: ['five-jewel', 'five-twinkle'], premium: ['five-jewel'],
})

export const HIGH_NOON_FORTUNE_SOUNDS = createThemedSlotSounds('high-noon-fortune-audio-v1', {
  ambienceSource: countryTrailSource, ambienceVolume: 0.06,
  leverSource: leverRegisterSource, leverVolume: 0.38,
  bonus: ['five-casino-win', 'five-jewel'], five: ['five-casino-win', 'premium-win'], premium: ['five-casino-win'],
})

export const GODS_OF_OLYMPUS_SOUNDS = createThemedSlotSounds('gods-of-olympus-audio-v1', {
  ambienceSource: ancientSwordSource, ambienceVolume: 0.055,
  leverSource: leverCoinSource, leverVolume: 0.3,
  bonus: ['five-jewel', 'premium-win'], five: ['premium-win', 'five-twinkle'], premium: ['five-jewel'],
})

export const PIRATES_FORTUNE_SOUNDS: SlotSoundSet = {
  ...DEFAULT_SLOT_SOUNDS,
  id: 'pirates-fortune-audio-v9',
  cues: {
    ...DEFAULT_SLOT_SOUNDS.cues,
    // Every event Pirates plays comes from its dedicated palette rather than
    // sharing generic sounds with other slot cabinets.
    // Pirate Voyage is the game music for manual and auto spins.
    // Waves are reserved for a single ordinary losing-spin result below.
    ambience: { source: pirateVoyageSource, baseVolume: 0.05, category: 'effect', loop: true },
    'auto-spin-ambience': { source: pirateVoyageSource, baseVolume: 0.05, category: 'effect', loop: true },
    'lever-pull': { source: pirateHelmSpinSource, baseVolume: 0.18, category: 'effect' },
    'reel-spin': { source: pirateHelmSpinSource, baseVolume: 0.12, category: 'effect', loop: true },
    'reel-stop': { source: pirateHelmSpinSource, baseVolume: 0.1, category: 'effect' },
    // A Broadside Run drops the regular voyage palette entirely. Its faster,
    // brighter reel and result cues make the special game audibly distinct.
    'special-reel-spin': { source: reelSpinSource, baseVolume: 0.28, category: 'effect', loop: true },
    'special-reel-stop': { source: reelStopSource, baseVolume: 0.44, category: 'effect' },
    'special-low-win': { source: fiveJewelSource, baseVolume: 0.46, category: 'result' },
    'special-premium-win': { source: fiveCasinoWinSource, baseVolume: 0.52, category: 'result' },
    'special-five-casino-win': { source: premiumWinSource, baseVolume: 0.54, category: 'result' },
    'special-five-jewel': { source: fiveTwinkleSource, baseVolume: 0.48, category: 'result' },
    'special-five-twinkle': { source: fiveJewelSource, baseVolume: 0.5, category: 'result' },
    'special-soft-miss': { source: pirateBeachWavesSource, baseVolume: 0.14, category: 'result' },
    // A single wave marks an ordinary manual loss. It is never a loop and is
    // suppressed for auto spins by the spin controller.
    // The former generated doubloon chime was too thin and harsh for an
    // ordinary win. This established low-win cue is warmer and restrained.
    'low-win': { source: lowWinSource, baseVolume: 0.28, category: 'result' },
    'premium-win': { source: premiumWinSource, baseVolume: 0.38, category: 'result' },
    'five-casino-win': { source: fiveCasinoWinSource, baseVolume: 0.4, category: 'result' },
    'five-jewel': { source: pirateSeagullWinSource, baseVolume: 0.17, category: 'result' },
    'five-twinkle': { source: pirateSeagullMissSource, baseVolume: 0.15, category: 'result' },
    'soft-miss': { source: pirateBeachWavesSource, baseVolume: 0.14, category: 'result' },
    'pirate-seagull-win': { source: pirateSeagullWinSource, baseVolume: 0.17, category: 'result' },
  },
  events: {
    ...DEFAULT_SLOT_SOUNDS.events,
    autoSpinAmbience: 'auto-spin-ambience',
    specialReelSpin: 'special-reel-spin',
    specialReelStop: 'special-reel-stop',
    results: {
      // A compact doubloon chime confirms ordinary wins while the two gulls
      // decorate larger outcomes.
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

export const ROYAL_DRAW_SOUNDS = createThemedSlotSounds('royal-draw-audio-v1', {
  ambienceSource: casinoRockSource, ambienceVolume: 0.05,
  leverSource: leverCoinSource, leverVolume: 0.28,
  bonus: ['five-twinkle', 'five-jewel'], five: ['five-jewel', 'premium-win'], premium: ['five-twinkle'],
})

export const SAMURAI_FORTUNE_SOUNDS = createThemedSlotSounds('samurai-fortune-audio-v1', {
  ambienceSource: asianDragonSource, ambienceVolume: 0.055,
  leverSource: leverCoinSource, leverVolume: 0.29,
  bonus: ['five-jewel', 'premium-win'], five: ['five-twinkle', 'five-jewel'], premium: ['five-jewel'],
})

export const ROBOT_REVOLUTION_SOUNDS = createThemedSlotSounds('robot-revolution-audio-v1', {
  ambienceSource: cyberNightSource, ambienceVolume: 0.05,
  leverSource: leverRegisterSource, leverVolume: 0.25,
  bonus: ['premium-win', 'five-twinkle'], five: ['five-jewel', 'premium-win'], premium: ['five-twinkle'],
})

export const PHANTOM_MANOR_SOUNDS = createThemedSlotSounds('phantom-manor-audio-v1', {
  ambienceSource: hauntedChimesSource, ambienceVolume: 0.04,
  leverSource: leverCoinSource, leverVolume: 0.23,
  bonus: ['five-twinkle', 'premium-win'], five: ['five-twinkle', 'five-jewel'], premium: ['premium-win'],
})

export const OCEAN_ODYSSEY_SOUNDS = createThemedSlotSounds('ocean-odyssey-audio-v1', {
  ambienceSource: pirateVoyageSource, ambienceVolume: 0.045,
  leverSource: leverCoinSource, leverVolume: 0.27,
  bonus: ['five-jewel', 'five-twinkle'], five: ['premium-win', 'five-jewel'], premium: ['five-jewel'],
})

export const DRAGON_HOARD_SOUNDS = createThemedSlotSounds('dragon-hoard-audio-v1', {
  ambienceSource: ancientSwordSource, ambienceVolume: 0.055,
  leverSource: leverRegisterSource, leverVolume: 0.36,
  bonus: ['five-casino-win', 'premium-win'], five: ['five-casino-win', 'five-jewel'], premium: ['five-casino-win'],
})

export const JUNGLE_JACKPOT_SOUNDS = createThemedSlotSounds('jungle-jackpot-audio-v1', {
  ambienceSource: forestJourneySource, ambienceVolume: 0.05,
  leverSource: leverCoinSource, leverVolume: 0.32,
  bonus: ['five-jewel', 'premium-win'], five: ['five-casino-win', 'five-jewel'], premium: ['five-jewel'],
})

export const CANDY_CARNIVAL_SOUNDS = createThemedSlotSounds('candy-carnival-audio-v1', {
  ambienceSource: carnivalLightsSource, ambienceVolume: 0.05,
  leverSource: leverCoinSource, leverVolume: 0.31,
  bonus: ['five-twinkle', 'five-jewel'], five: ['five-twinkle', 'premium-win'], premium: ['five-twinkle'],
})

export const DESERT_TREASURES_SOUNDS = createThemedSlotSounds('desert-treasures-audio-v1', {
  ambienceSource: desertWindSource, ambienceVolume: 0.04,
  leverSource: leverRegisterSource, leverVolume: 0.34,
  bonus: ['five-casino-win', 'five-jewel'], five: ['premium-win', 'five-casino-win'], premium: ['five-jewel'],
})

export const NEON_NIGHTS_SOUNDS = createThemedSlotSounds('neon-nights-audio-v1', {
  ambienceSource: cyberNightSource, ambienceVolume: 0.055,
  leverSource: leverRegisterSource, leverVolume: 0.24,
  bonus: ['five-twinkle', 'premium-win'], five: ['five-jewel', 'five-twinkle'], premium: ['premium-win'],
})

export const NORDIC_LEGENDS_SOUNDS = createThemedSlotSounds('nordic-legends-audio-v1', {
  ambienceSource: nordicAtmosphereSource, ambienceVolume: 0.05,
  leverSource: leverCoinSource, leverVolume: 0.3,
  bonus: ['five-jewel', 'premium-win'], five: ['five-casino-win', 'five-jewel'], premium: ['five-casino-win'],
})

export const WUKONG_TREASURES_SOUNDS: SlotSoundSet = {
  ...DEFAULT_SLOT_SOUNDS,
  id: 'wukong-treasures-audio-v2',
  cues: {
    ...DEFAULT_SLOT_SOUNDS.cues,
    ambience: { source: asianDragonSource, baseVolume: 0.06, category: 'effect', loop: true },
    'lever-pull': { source: reelSpinSource, baseVolume: 0.12, category: 'effect' },
    'reel-spin': { source: reelSpinSource, baseVolume: 0.11, category: 'effect', loop: true },
    'reel-stop': { source: noWinWaterDropSource, baseVolume: 0.15, category: 'effect' },
    'low-win': { source: fiveJewelSource, baseVolume: 0.21, category: 'result' },
    'premium-win': { source: fiveTwinkleSource, baseVolume: 0.28, category: 'result' },
    'five-casino-win': { source: fiveJewelSource, baseVolume: 0.32, category: 'result' },
    'five-jewel': { source: fiveJewelSource, baseVolume: 0.29, category: 'result' },
    'five-twinkle': { source: fiveTwinkleSource, baseVolume: 0.27, category: 'result' },
    'soft-miss': { source: noWinWaterDropSource, baseVolume: 0.13, category: 'result' },
  },
  events: {
    ...DEFAULT_SLOT_SOUNDS.events,
    results: {
      bonus: ['five-twinkle', 'five-jewel'],
      five: ['five-jewel', 'five-twinkle'],
      'no-win': ['soft-miss'],
      premium: ['five-twinkle'],
      'single-three': ['low-win'],
    },
  },
}

export const RAINBOW_REALM_SOUNDS = createThemedSlotSounds('rainbow-realm-audio-v1', {
  ambienceSource: carnivalLightsSource, ambienceVolume: 0.05,
  leverSource: leverCoinSource, leverVolume: 0.29,
  bonus: ['five-twinkle', 'five-jewel'], five: ['five-twinkle', 'premium-win'], premium: ['five-twinkle'],
})

export const ARCANE_ARCHIVES_SOUNDS = createThemedSlotSounds('arcane-archives-audio-v1', {
  ambienceSource: hauntedChimesSource, ambienceVolume: 0.035,
  leverSource: leverCoinSource, leverVolume: 0.25,
  bonus: ['five-twinkle', 'premium-win'], five: ['five-jewel', 'five-twinkle'], premium: ['premium-win'],
})

export const DINO_DOMINION_SOUNDS = createThemedSlotSounds('dino-dominion-audio-v1', {
  ambienceSource: forestJourneySource, ambienceVolume: 0.05,
  leverSource: leverRegisterSource, leverVolume: 0.3,
  bonus: ['five-casino-win', 'five-jewel'], five: ['premium-win', 'five-jewel'], premium: ['five-jewel'],
})

export const REEL_RICHES_SOUNDS = createThemedSlotSounds('reel-riches-audio-v1', {
  ambienceSource: pirateVoyageSource, ambienceVolume: 0.045,
  leverSource: leverRegisterSource, leverVolume: 0.3,
  bonus: ['five-jewel', 'five-twinkle'], five: ['premium-win', 'five-jewel'], premium: ['five-jewel'],
})

import pirateHelmSpinSource from '../../../assets/slots/audio/pirate-helm-spin.ogg'
import pirateSeagullMissSource from '../../../assets/slots/audio/pirate-seagull-miss.wav'
import pirateSeagullWinSource from '../../../assets/slots/audio/pirate-seagull-win.wav'
import pirateBeachWavesSource from '../../../assets/slots/audio/pirate-wave-ambience.flac'
import reelRichesCastLandSource from '../../../assets/slots/audio/reel-riches-cast-land.wav'
import reelRichesLineTensionSource from '../../../assets/slots/audio/reel-riches-line-tension.wav'
import reelRichesSmallCatchSource from '../../../assets/slots/audio/reel-riches-small-catch.wav'
import reelRichesSpecialCollectSource from '../../../assets/slots/audio/reel-riches-special-collect.wav'
import reelRichesTrophyAwardSource from '../../../assets/slots/audio/reel-riches-trophy-award.wav'
import reelRichesTrophyLoopSource from '../../../assets/slots/audio/reel-riches-trophy-loop.wav'
import reelRichesTrophyWinSource from '../../../assets/slots/audio/reel-riches-trophy-win.wav'
import wukongCelestialChimeSource from '../../../assets/slots/audio/wukong-celestial-chime.wav'
import wukongCloudRushSource from '../../../assets/slots/audio/wukong-cloud-rush.wav'
import wukongGongWinSource from '../../../assets/slots/audio/wukong-gong-win.wav'
import wukongSoftStepSource from '../../../assets/slots/audio/wukong-soft-step.wav'
import wukongStaffWhooshSource from '../../../assets/slots/audio/wukong-staff-whoosh.wav'
import wukongTempleBellSource from '../../../assets/slots/audio/wukong-temple-bell.wav'
import wukongWoodblockSource from '../../../assets/slots/audio/wukong-woodblock.wav'
import kenneyChipLaySource from '../../../assets/audio/cards/chip-lay-1.ogg'
import kenneyChipsHandleSource from '../../../assets/audio/cards/chips-handle-1.ogg'
import kenneyChipsCollideSource from '../../../assets/audio/cards/chips-collide-1.ogg'
import kenneyCardPlaceSource from '../../../assets/audio/cards/card-place-1.ogg'
import kenneyCardShuffleSource from '../../../assets/audio/cards/card-shuffle.ogg'
import { createThemedSlotAudio } from './themedSlotAudio'

// Preserve the recorded music selection without importing or emitting a raw
// track. These non-fetching identifiers are used only on disabled cues. See
// games/slots/shared/evidence/audio-provenance.md for the delivery decision.
const ancientSwordSource = 'disabled:opentracks/ancient-sword.mp3'
const asianDragonSource = 'disabled:opentracks/asian-dragon.mp3'
const carnivalLightsSource = 'disabled:opentracks/carnival-lights.mp3'
const casinoRockSource = 'disabled:opentracks/casino-rock.mp3'
const countryTrailSource = 'disabled:opentracks/country-trail.mp3'
const cyberNightSource = 'disabled:opentracks/cyber-night.mp3'
const desertWindSource = 'disabled:opentracks/desert-wind.mp3'
const forestJourneySource = 'disabled:opentracks/forest-journey.mp3'
const hauntedChimesSource = 'disabled:opentracks/haunted-chimes.mp3'
const nordicAtmosphereSource = 'disabled:opentracks/nordic-atmosphere.mp3'
const pirateVoyageSource = 'disabled:opentracks/pirate-voyage.mp3'

// Semantic aliases keep the event palettes and sound-set identities stable.
// The former generic slot WAVs had no located source-specific provenance;
// these existing Kenney Casino Audio clips have a retained CC0 license.
const fiveCasinoWinSource = kenneyChipsCollideSource
const fiveJewelSource = kenneyChipsHandleSource
const fiveTwinkleSource = kenneyChipLaySource
const leverCoinSource = kenneyChipLaySource
const leverRegisterSource = kenneyChipsHandleSource
const lowWinSource = kenneyChipsHandleSource
const noWinWaterDropSource = kenneyCardPlaceSource
const premiumWinSource = kenneyChipsCollideSource
const reelSpinSource = kenneyCardShuffleSource
const reelStopSource = kenneyChipLaySource

export type AudioCategory = 'effect' | 'result'
type LegacySlotSoundCueId =
  | 'ambience'
  | 'auto-spin-ambience'
  | 'lever-pull'
  | 'reel-spin'
  | 'reel-stop'
  | 'special-reel-spin'
  | 'special-reel-stop'
  | 'special-ambience'
  | 'special-collect'
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
type ThemeSlotSoundCueId =
  | 'button-click'
  | 'bet-step'
  | 'spin-button-click'
  | 'collect-sync'
  | 'collect-rows'
  | 'collect-paw'
  | 'collect-rand'
  | 'triple-sevens'
  | 'bonus-entry'
  | 'bonus-complete'
export type SlotSoundCueId = LegacySlotSoundCueId | ThemeSlotSoundCueId
export type SlotResultSoundEvent = 'bonus' | 'five' | 'no-win' | 'premium' | 'single-three'

export type SlotSoundCue = {
  source: string
  baseVolume: number
  category: AudioCategory
  loop?: boolean
  enabled?: boolean
}

export type SlotSoundSet = {
  id: string
  cues: Readonly<Record<LegacySlotSoundCueId, SlotSoundCue> & Partial<Record<ThemeSlotSoundCueId, SlotSoundCue>>>
  events: {
    ambience: SlotSoundCueId
    buttonClick?: SlotSoundCueId
    spinButtonClick?: SlotSoundCueId
    stopButtonClick?: SlotSoundCueId
    autoSpinAmbience?: SlotSoundCueId
    leverPull: SlotSoundCueId
    reelSpin: SlotSoundCueId
    reelStop: SlotSoundCueId
    specialReelSpin?: SlotSoundCueId
    specialReelStop?: SlotSoundCueId
    specialAmbience?: SlotSoundCueId
    specialCollect?: SlotSoundCueId
    /** Explicit opt-in; legacy/excluded cabinets retain their event behavior. */
    themeSuite?: true
    betStep?: SlotSoundCueId
    collectionArrival?: Readonly<Partial<Record<'sync' | 'rows' | 'paw' | 'rand', SlotSoundCueId>>>
    tripleSevens?: SlotSoundCueId
    bonusComplete?: SlotSoundCueId
    results: Readonly<Record<SlotResultSoundEvent, readonly SlotSoundCueId[]>>
    specialResults?: Readonly<Record<SlotResultSoundEvent, readonly SlotSoundCueId[]>>
  }
}

// Sound identities, events and levels remain the backwards-compatible default.
// Ambience stays disabled until its raw-file delivery terms are resolved.
export const DEFAULT_SLOT_SOUNDS: SlotSoundSet = {
  id: 'fortune-forge-default-audio-v2',
  cues: {
    ambience: { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true, enabled: false },
    'auto-spin-ambience': { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true, enabled: false },
    'lever-pull': { source: leverRegisterSource, baseVolume: 0.34, category: 'effect' },
    'reel-spin': { source: reelSpinSource, baseVolume: 0.14, category: 'effect', loop: false },
    'reel-stop': { source: reelStopSource, baseVolume: 0.22, category: 'effect' },
    'special-reel-spin': { source: reelSpinSource, baseVolume: 0.18, category: 'effect', loop: true },
    'special-reel-stop': { source: reelStopSource, baseVolume: 0.3, category: 'effect' },
    'special-ambience': { source: casinoRockSource, baseVolume: 0.055, category: 'effect', loop: true, enabled: false },
    'special-collect': { source: fiveTwinkleSource, baseVolume: 0.26, category: 'effect' },
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
  const base: SlotSoundSet = {
    ...DEFAULT_SLOT_SOUNDS,
    id,
    cues: {
      ...DEFAULT_SLOT_SOUNDS.cues,
      ambience: {
        source: options.ambienceSource,
        baseVolume: options.ambienceVolume,
        category: 'effect',
        loop: true,
        enabled: false,
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
  return createThemedSlotAudio(id.replace(/-audio-v\d+$/, ''), base)
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
    // Retain the selected voyage identity while its delivery is unresolved.
    // The CC0 helm, gull and wave effects remain in the active palette.
    ambience: { source: pirateVoyageSource, baseVolume: 0.05, category: 'effect', loop: true, enabled: false },
    'auto-spin-ambience': { source: pirateVoyageSource, baseVolume: 0.05, category: 'effect', loop: true, enabled: false },
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

const midnightVaultBaseSounds = createThemedSlotSounds('dragon-hoard-audio-v1', {
  // Midnight Vault retains the historical sound-set ID and semantic events.
  // Official CC BY 4.0 noir score, credited in the game's help and asset folder.
  ambienceSource: '/assets/slots/midnight-vault/v3/audio/i-knew-a-guy-kevin-macleod.mp3', ambienceVolume: 0.04,
  leverSource: kenneyChipLaySource, leverVolume: 0.26,
  bonus: ['five-jewel'], five: ['five-twinkle'], premium: ['premium-win'],
})

export const DRAGON_HOARD_SOUNDS: SlotSoundSet = {
  ...midnightVaultBaseSounds,
  cues: {
    ...midnightVaultBaseSounds.cues,
    // Unlike the retired DOVA mapping, this CC BY recording permits direct
    // distribution with attribution. Playback starts after player interaction.
    ambience: { ...midnightVaultBaseSounds.cues.ambience, enabled: true },
    // Every currently reachable effect/result uses the existing Kenney CC0
    // palette. Preserve the reel cue categories and loop flags exactly.
    'reel-spin': { ...DEFAULT_SLOT_SOUNDS.cues['reel-spin'], source: kenneyCardShuffleSource, baseVolume: 0.1 },
    'reel-stop': { ...DEFAULT_SLOT_SOUNDS.cues['reel-stop'], source: kenneyChipLaySource, baseVolume: 0.16 },
    'low-win': { source: kenneyChipsHandleSource, baseVolume: 0.22, category: 'result' },
    'premium-win': { source: kenneyChipsCollideSource, baseVolume: 0.28, category: 'result' },
    'five-jewel': { source: kenneyChipsCollideSource, baseVolume: 0.3, category: 'result' },
    'five-twinkle': { source: kenneyChipsHandleSource, baseVolume: 0.3, category: 'result' },
    'soft-miss': { source: kenneyCardPlaceSource, baseVolume: 0.08, category: 'result' },
  },
}

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
  id: 'wukong-treasures-audio-v3',
  cues: {
    ...DEFAULT_SLOT_SOUNDS.cues,
    ambience: { source: asianDragonSource, baseVolume: 0.06, category: 'effect', loop: true, enabled: false },
    'lever-pull': { source: wukongStaffWhooshSource, baseVolume: 0.24, category: 'effect' },
    'reel-spin': { source: wukongCloudRushSource, baseVolume: 0.1, category: 'effect', loop: true },
    'reel-stop': { source: wukongWoodblockSource, baseVolume: 0.2, category: 'effect' },
    'low-win': { source: wukongTempleBellSource, baseVolume: 0.23, category: 'result' },
    'premium-win': { source: wukongGongWinSource, baseVolume: 0.27, category: 'result' },
    'five-casino-win': { source: wukongGongWinSource, baseVolume: 0.3, category: 'result' },
    'five-jewel': { source: wukongCelestialChimeSource, baseVolume: 0.26, category: 'result' },
    'five-twinkle': { source: wukongCelestialChimeSource, baseVolume: 0.24, category: 'result' },
    'soft-miss': { source: wukongSoftStepSource, baseVolume: 0.1, category: 'result' },
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

const RAINBOW_REALM_BASE_SOUNDS = createThemedSlotSounds('rainbow-realm-audio-v1', {
  ambienceSource: carnivalLightsSource, ambienceVolume: 0.05,
  leverSource: leverCoinSource, leverVolume: 0.29,
  bonus: ['five-twinkle', 'five-jewel'], five: ['five-twinkle', 'premium-win'], premium: ['five-twinkle'],
})

export const RAINBOW_REALM_SOUNDS: SlotSoundSet = createThemedSlotAudio('rainbow-realm', {
  ...RAINBOW_REALM_BASE_SOUNDS,
  id: 'rainbow-realm-audio-v2',
  cues: {
    ...RAINBOW_REALM_BASE_SOUNDS.cues,
    // Retain the Orchard Rush palette while honoring the shared unresolved
    // music policy. Its result and reel effects use existing cleared clips.
    'special-ambience': { source: forestJourneySource, baseVolume: 0.14, category: 'effect', loop: true, enabled: false },
    'special-reel-spin': { source: wukongCloudRushSource, baseVolume: 0.22, category: 'effect', loop: true },
    'special-reel-stop': { source: wukongWoodblockSource, baseVolume: 0.31, category: 'effect' },
    'special-low-win': { source: wukongCelestialChimeSource, baseVolume: 0.38, category: 'result' },
    'special-premium-win': { source: fiveCasinoWinSource, baseVolume: 0.44, category: 'result' },
    'special-five-jewel': { source: fiveJewelSource, baseVolume: 0.39, category: 'result' },
    'special-five-twinkle': { source: fiveTwinkleSource, baseVolume: 0.38, category: 'result' },
    'special-soft-miss': { source: wukongSoftStepSource, baseVolume: 0.16, category: 'result' },
  },
  events: {
    ...RAINBOW_REALM_BASE_SOUNDS.events,
    specialAmbience: 'special-ambience',
    specialReelSpin: 'special-reel-spin',
    specialReelStop: 'special-reel-stop',
    specialResults: {
      bonus: ['special-premium-win', 'special-five-jewel'],
      five: ['special-premium-win', 'special-five-twinkle'],
      'no-win': ['special-soft-miss'],
      premium: ['special-premium-win', 'special-five-jewel'],
      'single-three': ['special-low-win'],
    },
  },
})

export const ARCANE_ARCHIVES_SOUNDS = createThemedSlotSounds('arcane-archives-audio-v1', {
  ambienceSource: hauntedChimesSource, ambienceVolume: 0.035,
  leverSource: leverCoinSource, leverVolume: 0.25,
  bonus: ['five-twinkle', 'premium-win'], five: ['five-jewel', 'five-twinkle'], premium: ['premium-win'],
})

const reelRichesBaseSounds = createThemedSlotSounds('reel-riches-audio-v3', {
  ambienceSource: pirateVoyageSource, ambienceVolume: 0.045,
  leverSource: leverRegisterSource, leverVolume: 0.3,
  bonus: ['five-casino-win'], five: ['premium-win', 'five-jewel'], premium: ['five-jewel'],
})

export const REEL_RICHES_SOUNDS: SlotSoundSet = createThemedSlotAudio('reel-riches', {
  ...reelRichesBaseSounds,
  cues: {
    ...reelRichesBaseSounds.cues,
    // The feature arrangement replaces the lake voyage only during free casts.
    'special-ambience': { source: reelRichesTrophyLoopSource, baseVolume: 0.13, category: 'effect', loop: true },
    'special-reel-spin': { source: reelRichesLineTensionSource, baseVolume: 0.12, category: 'effect', loop: true },
    'special-reel-stop': { source: reelRichesCastLandSource, baseVolume: 0.25, category: 'effect' },
    'special-collect': { source: reelRichesSpecialCollectSource, baseVolume: 0.27, category: 'effect' },
    'five-casino-win': { source: reelRichesTrophyAwardSource, baseVolume: 0.4, category: 'result' },
    'special-low-win': { source: reelRichesSmallCatchSource, baseVolume: 0.24, category: 'result' },
    'special-premium-win': { source: reelRichesTrophyWinSource, baseVolume: 0.38, category: 'result' },
    'special-five-casino-win': { source: reelRichesTrophyWinSource, baseVolume: 0.43, category: 'result' },
    'special-five-jewel': { source: reelRichesTrophyWinSource, baseVolume: 0.34, category: 'result' },
    'special-five-twinkle': { source: reelRichesTrophyWinSource, baseVolume: 0.32, category: 'result' },
    'special-soft-miss': { source: noWinWaterDropSource, baseVolume: 0.14, category: 'result' },
  },
  events: {
    ...reelRichesBaseSounds.events,
    specialAmbience: 'special-ambience',
    specialReelSpin: 'special-reel-spin',
    specialReelStop: 'special-reel-stop',
    specialCollect: 'special-collect',
    results: {
      ...reelRichesBaseSounds.events.results,
      bonus: ['five-casino-win'],
    },
    specialResults: {
      bonus: ['special-five-casino-win'],
      five: ['special-five-casino-win'],
      'no-win': ['special-soft-miss'],
      premium: ['special-premium-win'],
      'single-three': ['special-low-win'],
    },
  },
})

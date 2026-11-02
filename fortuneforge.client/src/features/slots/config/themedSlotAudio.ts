import type { SlotSoundCue, SlotSoundSet } from './soundSets'

export const THEMED_AUDIO_GAME_SLUGS = [
  'cosmic-fortune', 'high-noon-fortune', 'gods-of-olympus', 'royal-draw', 'samurai-fortune',
  'robot-revolution', 'phantom-manor', 'ocean-odyssey', 'jungle-jackpot', 'candy-carnival',
  'desert-treasures', 'neon-nights', 'nordic-legends', 'rainbow-realm', 'arcane-archives',
  'dino-dominion', 'reel-riches',
] as const

/** Only these seventeen titles opt in; old sound-set IDs remain stable. */
export function createThemedSlotAudio(slug: string, previous: SlotSoundSet): SlotSoundSet {
  if (!(THEMED_AUDIO_GAME_SLUGS as readonly string[]).includes(slug)) return previous
  const cue = (id: string, baseVolume: number, category: SlotSoundCue['category'] = 'effect', loop = false): SlotSoundCue => ({
    source: `/assets/slots/audio-suites/v1/${slug}/${id}.ogg`, baseVolume, category, loop, enabled: true,
  })
  const results: SlotSoundSet['events']['results'] = {
    bonus: ['bonus-entry'], five: ['five-casino-win'], premium: ['premium-win'],
    'single-three': ['low-win'], 'no-win': ['soft-miss'],
  }
  return {
    ...previous,
    cues: {
      ...previous.cues,
      // Match the suite manifests' audition mix, then apply saved user volume.
      ambience: cue('night-drive', .62, 'effect', true),
      'special-ambience': cue('midnight-mix', .60, 'effect', true),
      'button-click': cue('button-click', .58), 'bet-step': cue('bet-step', .54),
      'lever-pull': cue('spin-launch', .65), 'reel-spin': cue('reel-roll', .40, 'effect', true),
      'reel-stop': cue('reel-land', .62),
      'special-reel-spin': cue('bonus-reel-roll', .40, 'effect', true),
      'special-reel-stop': cue('bonus-reel-land', .60),
      'collect-sync': cue('collect-flamingo', .58), 'collect-rows': cue('collect-dice', .58),
      'collect-paw': cue('collect-sunset', .58), 'collect-rand': cue('collect-arcade', .58),
      'low-win': cue('win-small', .65, 'result'), 'premium-win': cue('win-medium', slug === 'neon-nights' ? .67 : .65, 'result'),
      'five-casino-win': cue('win-big', .70, 'result'), 'triple-sevens': cue('triple-sevens', .67, 'result'),
      'soft-miss': cue('no-win', .30, 'result'), 'bonus-entry': cue('bonus-entry', .68, 'result'),
      'bonus-complete': cue('bonus-complete', .66, 'result'),
    },
    // No auto-music layer or old multi-stinger/special-collect mappings survive.
    // Track names are engine modes; asset names are shared delivery identities.
    events: {
      themeSuite: true, ambience: 'ambience', specialAmbience: 'special-ambience',
      buttonClick: 'button-click', betStep: 'bet-step', leverPull: 'lever-pull',
      reelSpin: 'reel-spin', reelStop: 'reel-stop', specialReelSpin: 'special-reel-spin', specialReelStop: 'special-reel-stop',
      collectionArrival: { sync: 'collect-sync', rows: 'collect-rows', paw: 'collect-paw', rand: 'collect-rand' },
      tripleSevens: 'triple-sevens', bonusComplete: 'bonus-complete', results, specialResults: results,
    },
  }
}

import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { SAMURAI_FORTUNE_SOUNDS } from '../../../features/slots/config/soundSets'
import { SAMURAI_FORTUNE_SYMBOL_IMAGES as symbols } from './symbols'

export const SAMURAI_FORTUNE_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'samurai-fortune', title: 'Samurai Fortune', subtitle: 'Honor Meets the Rising Sun',
  description: 'Cross a lantern-lit province of blossom gardens, guarded castles, and legendary blades.',
  serverGameId: 'samurai-fortune-v1',
  paylinePatternIds: Array.from({ length: 23 }, (_, index) => index + 1),
  presentation: 'seal-pile', collectionAriaLabel: 'Clan crest collections', itemLabel: 'clan crests',
  energyLabel: 'Spirit charge', actorName: 'The shogun treasure satchel', awardLabel: 'Clan treasury haul',
  outcomeNarrative: {
    lossTitle: 'No honor won this spin', winTitle: 'Honor won', greatWinTitle: 'Warrior reward', bigWinTitle: 'Shogun jackpot',
    freeGameSingular: 'free duel', freeGamePlural: 'free duels',
    lossNextAction: 'Choose a wager, then challenge the province again.',
    winNextAction: 'Choose a wager, then seek another warrior reward.',
    bonusNextAction: 'Your next duel is free and uses the locked wager.',
  },
  celebrationEffect: 'samurai-blossom',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['golden mon coin', '🪙', symbols.VALUE], motif: '⛩️', accentGlyph: '🌸',
  collectionLabels: [['Crimson crane crest', '🦢', symbols.SEAL_SYNC], ['Sapphire moon crest', '🌙', symbols.SEAL_ROWS], ['Amber sun crest', '☀️', symbols.SEAL_PAW], ['Jade dragon crest', '🐉', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Ceremonial rice bowl', '🍚', symbols['2']], '3': ['Painted folding fan', '🪭', symbols['3']], '4': ['Festival lantern', '🏮', symbols['4']],
    '5': ['Cherry blossom', '🌸', symbols['5']], '6': ['Forged katana', '🗡️', symbols['6']], '7': ['Mountain shogun castle', '🏯', symbols['7']],
    ACE: ['Dragon mask wild', '🐉', symbols.ACE], FREE: ['Sacred torii free game', '⛩️', symbols.FREE], POWER: ['Jade spirit orb', '🟢', symbols.POWER],
    BOLT: ['Storm spirit charge', '⚡', symbols.BOLT], BANANA: ['Triple shuriken', '🥷', symbols.BANANA], PAW: ['Shogun treasure satchel', '🎒', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 26, collectionAwardedSpins: 7,
    freeGames: { requiredSymbols: 4, awardedSpins: 6 }, sounds: SAMURAI_FORTUNE_SOUNDS,
    usesEnergy: false, usesDirectValueTokens: false,
    feature: {
      id: 'samurai-blade-trial', title: 'Blade Trial',
      earnLabel: 'Land 4 torii gates for 6 Blade Trials, or complete 26 crests for 7 enhanced trials.',
      earnStyle: 'stack', earnHint: 'Clan crests stack on one of four dojo racks; a full rack opens a Blade Trial.',
    activeModes: {
      sync: 'Twin Blades · a matching reel is copied', rows: 'Rising Sun · two extra rows',
      paw: 'Ronin’s Cache · extra wilds appear', rand: 'Shogun’s Banner · a wild reel appears',
      'sync-paw': 'Twin Blades · linked reels with a wild burst',
    },
    },
  },
  colors: { skyTop: '#341018', skyBottom: '#9d3b45', horizon: '#492023', ground: '#180b0c', primary: '#a51f2d', secondary: '#f0b8bb', deep: '#2b0b10', rim: '#e6bd63', glow: '#ff8a94', text: '#fff1e7' },
}

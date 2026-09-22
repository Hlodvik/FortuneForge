import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { JUNGLE_JACKPOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { JUNGLE_JACKPOT_SYMBOL_IMAGES as symbols } from './symbols'

export const JUNGLE_JACKPOT_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'jungle-jackpot', title: 'Jungle Jackpot', subtitle: 'Find the Golden Temple',
  description: 'Push through a living rainforest where bright wildlife guards an overgrown golden temple.',
  serverGameId: 'jungle-jackpot-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 16, 17, 18, 19, 20, 21, 22, 23],
  presentation: 'fossil-dig', collectionAriaLabel: 'Rainforest relic collections', itemLabel: 'jungle relics',
  energyLabel: 'Sunbeam charge', actorName: 'The explorer field pack', awardLabel: 'Temple expedition haul',
  outcomeNarrative: {
    lossTitle: 'No temple haul this spin', winTitle: 'Temple haul', greatWinTitle: 'Golden discovery', bigWinTitle: 'Jungle jackpot',
    freeGameSingular: 'free expedition', freeGamePlural: 'free expeditions',
    lossNextAction: 'Choose a wager, then explore the jungle again.',
    winNextAction: 'Choose a wager, then set out for another temple haul.',
    bonusNextAction: 'Your next expedition is free and uses the locked wager.',
  },
  celebrationEffect: 'jungle-canopy',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['golden temple coin', '🪙', symbols.VALUE], motif: '🌴', accentGlyph: '🦜',
  collectionLabels: [['Jaguar fang relic', '🐆', symbols.SEAL_SYNC], ['Blue orchid relic', '🪻', symbols.SEAL_ROWS], ['Amber sun relic', '☀️', symbols.SEAL_PAW], ['Emerald frog relic', '🐸', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Tropical leaf', '🍃', symbols['2']], '3': ['Scarlet parrot', '🦜', symbols['3']], '4': ['Tree frog', '🐸', symbols['4']],
    '5': ['Playful monkey', '🐒', symbols['5']], '6': ['Rainforest jaguar', '🐆', symbols['6']], '7': ['Golden jungle temple', '🛕', symbols['7']],
    ACE: ['Golden tiger wild', '🐅', symbols.ACE], FREE: ['Hidden waterfall free game', '🏞️', symbols.FREE], POWER: ['Radiant sun idol', '☀️', symbols.POWER],
    BOLT: ['Firefly charge', '✨', symbols.BOLT], BANANA: ['Triple jungle vines', '🌿', symbols.BANANA], PAW: ['Explorer field pack', '🎒', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 22, collectionAwardedSpins: 8,
    freeGames: { requiredSymbols: 3, awardedSpins: 6 }, sounds: JUNGLE_JACKPOT_SOUNDS,
    usesEnergy: false, usesDirectValueTokens: false,
    feature: {
      id: 'jungle-temple-trek', title: 'Temple Trek',
      earnLabel: 'Land 3 hidden waterfalls for 6 Temple Treks, or complete 22 relics for 8 enhanced treks.',
      earnStyle: 'dig', earnHint: 'Jungle relics build an expedition trail; complete a trail to unlock a Temple Trek.',
    activeModes: {
      sync: 'Vine Link · a matching reel is copied', rows: 'Canopy Rise · two extra rows',
      paw: 'Explorer’s Find · extra wilds appear', rand: 'Golden Temple · a wild reel appears',
      'sync-rows-paw': 'Temple Ascension · linked reels, extra rows, and wilds',
    },
    },
  },
  colors: { skyTop: '#082f2b', skyBottom: '#1f6b43', horizon: '#17492d', ground: '#071d15', primary: '#3a8f3b', secondary: '#f2b94b', deep: '#092719', rim: '#ffe07a', glow: '#8be34f', text: '#f4ffe6' },
}

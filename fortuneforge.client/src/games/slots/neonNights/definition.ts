import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { NEON_NIGHTS_SOUNDS } from '../../../features/slots/config/soundSets'
import { NEON_NIGHTS_SYMBOL_IMAGES as symbols } from './symbols'

export const NEON_NIGHTS_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'neon-nights',
  title: 'Neon Nights',
  subtitle: 'Light Up the Midnight Jackpot',
  description: 'Race through an electric city of arcade lights, synth beats, and glowing midnight prizes.',
  serverGameId: 'neon-nights-v1',
  // Neon-only idle artwork replaces the disabled energy stop with the accepted car.
  initialReels: [
    ['2', '3', '4', '6'],
    ['3', '4', '5', '6'],
    ['4', '5', '6', '7'],
    ['5', '6', '7', 'ACE'],
    ['FREE', '7', 'ACE', 'POWER'],
  ],
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 17, 18, 19, 20, 21, 22, 23],
  presentation: 'star-orbit',
  collectionAriaLabel: 'Neon district collections',
  itemLabel: 'collection symbols',
  energyLabel: 'Voltage charge',
  actorName: 'The midnight DJ deck',
  awardLabel: 'Neon remix haul',
  outcomeNarrative: {
    lossTitle: 'No neon win this spin', winTitle: 'Neon win', greatWinTitle: 'After-dark win', bigWinTitle: 'Midnight jackpot',
    freeGameSingular: 'free night spin', freeGamePlural: 'free night spins',
    lossNextAction: 'Choose a wager, then light up the city again.',
    winNextAction: 'Choose a wager, then chase another neon win.',
    bonusNextAction: 'Your next night spin is free and uses the locked wager.',
  },
  celebrationEffect: 'neon-scan',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['glowing arcade chip', '💿', symbols.VALUE],
  motif: '🌃',
  accentGlyph: '🎵',
  collectionLabels: [['Pink flamingo', '🦩', symbols.SEAL_SYNC], ['Blue dice', '🎲', symbols.SEAL_ROWS], ['Neon sunset', '🌅', symbols.SEAL_PAW], ['Arcade cabinet', '🕹️', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Ornamental J', 'J', symbols['2']], '3': ['Retro cassette', '📼', symbols['3']], '4': ['Ornamental Q', 'Q', symbols['4']],
    '5': ['Ornamental K', 'K', symbols['5']], '6': ['Midnight sports car', '🏎️', symbols['6']], '7': ['Ornamental A', 'A', symbols['7']],
    ACE: ['Diamond shades wild', '🕶️', symbols.ACE], FREE: ['Neon bonus sign', 'BONUS', symbols.FREE], POWER: ['Disco ball power', '🪩', symbols.POWER],
    BOLT: ['Voltage lightning', '⚡', symbols.BOLT], BANANA: ['Triple neon sevens', '7️⃣', symbols.BANANA], PAW: ['Midnight DJ deck', '🎛️', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 26, collectionAwardedSpins: 7,
    freeGames: { requiredSymbols: 3, awardedSpins: 6 }, sounds: NEON_NIGHTS_SOUNDS,
    usesEnergy: false, usesDirectValueTokens: false,
    feature: {
      id: 'neon-midnight-mix', title: 'Midnight Mix',
      earnLabel: 'Land 3 neon bonus signs for 6 Midnight Mixes, or collect 26 matching collection symbols for 7 enhanced mixes.',
      earnStyle: 'orbit', earnHint: 'Collection symbols light the dancefloor ring; a full ring unlocks a Midnight Mix.',
      activeModes: {
        sync: 'Beat Match · a matching reel is copied', rows: 'Laser Wall · two extra rows',
        paw: 'DJ Drop · extra wilds appear', rand: 'Arcade Rush · a wild reel appears',
      },
    },
  },
  colors: { skyTop: '#070b2d', skyBottom: '#34105c', horizon: '#091c43', ground: '#050819', primary: '#cf2cff', secondary: '#22e7ff', deep: '#16072e', rim: '#ffd34d', glow: '#ff4fd8', text: '#fff4ff' },
}

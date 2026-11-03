import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { NEON_NIGHTS_SOUNDS } from '../../../features/slots/config/soundSets'
import { NEON_NIGHTS_SYMBOL_IMAGES as symbols } from './symbols'

export const NEON_NIGHTS_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'neon-nights',
  title: 'Neon Nights',
  subtitle: 'Light Up the Midnight Jackpot',
  description: 'Race through an electric city of arcade lights, synth beats, and glowing midnight prizes.',
  serverGameId: 'neon-nights-v1',
  serverSymbolSetId: 'neon-arcade-symbols-v2',
  // Ordinary arcade symbols lead the board; feature icons are rare reel stops.
  initialReels: [
    ['SEAL_SYNC', '3', '4', 'SEAL_ROWS'],
    ['3', 'SEAL_PAW', '5', '2'],
    ['SEAL_ROWS', '4', 'SEAL_SYNC', '7'],
    ['5', 'SEAL_SYNC', '7', 'ACE'],
    ['SEAL_PAW', '7', '3', 'BANANA'],
  ],
  paylinePatternIds: Array.from({ length: 23 }, (_, index) => index + 1),
  presentation: 'star-orbit',
  collectionAriaLabel: 'Neon arcade symbols',
  itemLabel: 'arcade symbols',
  energyLabel: 'Voltage charge',
  actorName: 'The midnight DJ deck',
  awardLabel: 'Neon remix haul',
  outcomeNarrative: {
    lossTitle: 'No neon win this spin', winTitle: 'Neon win', greatWinTitle: 'After-dark win', bigWinTitle: 'Midnight jackpot',
    freeGameSingular: 'bonus spin', freeGamePlural: 'bonus spins',
    lossNextAction: 'Choose a wager, then light up the city again.',
    winNextAction: 'Choose a wager, then chase another neon win.',
    bonusNextAction: 'Special Plays use the wager that started the round.',
  },
  celebrationEffect: 'neon-scan',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['glowing arcade chip', '💿', symbols.VALUE],
  motif: '🌃',
  accentGlyph: '🎵',
  collectionLabels: [['Pink neon koi', '🐟', symbols.SEAL_SYNC], ['Blue dice', '🎲', symbols.SEAL_ROWS], ['Red neon lantern', '🏮', symbols.SEAL_PAW], ['Arcade cabinet feature', '🕹️', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Jack', 'J', symbols['2']], '3': ['Retro cassette', '📼', symbols['3']], '4': ['Queen', 'Q', symbols['4']],
    '5': ['King', 'K', symbols['5']], '6': ['Sports car feature', '🏎️', symbols['6']], '7': ['Ace', 'A', symbols['7']],
    ACE: ['Shades Wild', '🕶️', symbols.ACE], FREE: ['BONUS payline boost', 'BONUS', symbols.FREE], POWER: ['Disco ball feature', '🪩', symbols.POWER],
    BOLT: ['Voltage lightning', '⚡', symbols.BOLT], BANANA: ['Triple neon sevens', '7️⃣', symbols.BANANA], PAW: ['Midnight DJ deck', '🎛️', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 26, collectionAwardedSpins: 0,
    freeGames: { requiredSymbols: 3, awardedSpins: 0 }, sounds: NEON_NIGHTS_SOUNDS,
    usesCollections: false, usesEnergy: false, usesDirectValueTokens: false,
    feature: {
      id: 'neon-midnight-mix', title: 'Special Plays',
      earnLabel: 'Land a sports car, disco ball or arcade cabinet to win 7 bonus spins.',
      earnStyle: 'orbit', earnHint: 'Bonus spins use the wager that triggered them.',
      activeModes: {
        sync: 'Beat Match · a matching reel is copied', rows: 'Laser Wall · two extra rows',
        paw: 'DJ Drop · extra wilds appear', rand: 'Arcade Rush · a wild reel appears',
      },
    },
  },
  colors: { skyTop: '#070b2d', skyBottom: '#34105c', horizon: '#091c43', ground: '#050819', primary: '#cf2cff', secondary: '#22e7ff', deep: '#16072e', rim: '#ffd34d', glow: '#ff4fd8', text: '#fff4ff' },
}

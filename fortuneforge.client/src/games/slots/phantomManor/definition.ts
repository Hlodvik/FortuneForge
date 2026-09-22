import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { PHANTOM_MANOR_SOUNDS } from '../../../features/slots/config/soundSets'
import { PHANTOM_MANOR_SYMBOL_IMAGES as symbols } from './symbols'

export const PHANTOM_MANOR_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'phantom-manor', title: 'Phantom Manor', subtitle: 'Fortune Haunts These Halls',
  description: 'Unlock a moonlit estate of whispering portraits, restless ravens, and spectral treasure.',
  serverGameId: 'phantom-manor-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 18, 19, 20, 21, 22, 23],
  presentation: 'spellbook-shelf', collectionAriaLabel: 'Haunted portrait collections', itemLabel: 'spirit seals',
  energyLabel: 'Moonlight charge', actorName: 'The séance spirit lantern', awardLabel: 'Phantom treasure haul',
  outcomeNarrative: {
    lossTitle: 'No spirit reward this spin', winTitle: 'Haunted win', greatWinTitle: 'Spectral reward', bigWinTitle: 'Manor jackpot',
    freeGameSingular: 'free haunting', freeGamePlural: 'free hauntings',
    lossNextAction: 'Choose a wager, then enter the manor again.',
    winNextAction: 'Choose a wager, then call for another spectral reward.',
    bonusNextAction: 'Your next haunting is free and uses the locked wager.',
  },
  celebrationEffect: 'phantom-mist',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['captured spirit wisp', '👻', symbols.VALUE], motif: '🏚️', accentGlyph: '🌙',
  collectionLabels: [['Crimson raven seal', '🐦‍⬛', symbols.SEAL_SYNC], ['Sapphire mirror seal', '🪞', symbols.SEAL_ROWS], ['Amber candle seal', '🕯️', symbols.SEAL_PAW], ['Emerald key seal', '🗝️', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Whispering candle', '🕯️', symbols['2']], '3': ['Ancient skeleton key', '🗝️', symbols['3']], '4': ['Midnight raven', '🐦‍⬛', symbols['4']],
    '5': ['Haunted mirror', '🪞', symbols['5']], '6': ['Restless phantom', '👻', symbols['6']], '7': ['Moonlit manor', '🏚️', symbols['7']],
    ACE: ['Jeweled skull wild', '💀', symbols.ACE], FREE: ['Forbidden crypt free game', '🚪', symbols.FREE], POWER: ['Séance crystal power', '🔮', symbols.POWER],
    BOLT: ['Full-moon charge', '🌙', symbols.BOLT], BANANA: ['Triple shadow bats', '🦇', symbols.BANANA], PAW: ['Séance spirit lantern', '🏮', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 18, collectionAwardedSpins: 9,
    freeGames: { requiredSymbols: 3, awardedSpins: 5 }, sounds: PHANTOM_MANOR_SOUNDS,
    usesCollections: false, usesEnergy: false, usesDirectValueTokens: false,
    earnHelp: 'Land 3 forbidden crypt doors anywhere in a spin to begin five Midnight Séance Spins with extra wilds.',
    feature: {
      id: 'phantom-midnight-seance', title: 'Midnight Séance',
      earnLabel: 'Land 3 crypt doors for 5 Séance Spins.',
      earnStyle: 'gates', earnHint: 'Crypt doors unlock one by one; three revealed doors call a Midnight Séance.',
      activeModes: {
        sync: 'Mirror Haunt · a matching reel is copied', rows: 'Grand Hall · two extra rows',
        paw: 'Lantern Call · extra wilds appear', rand: 'Spectral Treasury · a wild reel appears',
      },
    },
  },
  colors: { skyTop: '#0d1028', skyBottom: '#332550', horizon: '#20203c', ground: '#080913', primary: '#5b4a8d', secondary: '#73d0ba', deep: '#151329', rim: '#c8b47a', glow: '#a58cff', text: '#f4f0ff' },
}

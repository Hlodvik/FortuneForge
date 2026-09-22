import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { OCEAN_ODYSSEY_SOUNDS } from '../../../features/slots/config/soundSets'
import { OCEAN_ODYSSEY_SYMBOL_IMAGES as symbols } from './symbols'

export const OCEAN_ODYSSEY_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'ocean-odyssey', title: 'Ocean Odyssey', subtitle: 'Dive Beyond the Blue',
  description: 'Descend through coral gardens and ancient currents in search of a luminous pearl kingdom.',
  serverGameId: 'ocean-odyssey-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 23],
  presentation: 'gem-hoard', collectionAriaLabel: 'Coral treasure collections', itemLabel: 'sea treasures',
  energyLabel: 'Tidal charge', actorName: 'The pearl-diver net', awardLabel: 'Deep-sea treasure haul',
  outcomeNarrative: {
    lossTitle: 'No deep-sea find this spin', winTitle: 'Deep-sea find', greatWinTitle: 'Oceanic reward', bigWinTitle: 'Abyssal jackpot',
    freeGameSingular: 'free dive', freeGamePlural: 'free dives',
    lossNextAction: 'Choose a wager, then dive again when ready.',
    winNextAction: 'Choose a wager, then search the deep again.',
    bonusNextAction: 'Your next dive is free and uses the locked wager.',
  },
  celebrationEffect: 'ocean-swell',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['luminous pearl', '🫧', symbols.VALUE], motif: '🌊', accentGlyph: '🐚',
  collectionLabels: [['Crimson coral treasure', '🪸', symbols.SEAL_SYNC], ['Sapphire shell treasure', '🐚', symbols.SEAL_ROWS], ['Amber star treasure', '⭐', symbols.SEAL_PAW], ['Emerald turtle treasure', '🐢', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Spiral seashell', '🐚', symbols['2']], '3': ['Golden starfish', '⭐', symbols['3']], '4': ['Striped tropical fish', '🐠', symbols['4']],
    '5': ['Playful dolphin', '🐬', symbols['5']], '6': ['Ancient sea turtle', '🐢', symbols['6']], '7': ['Great blue whale', '🐋', symbols['7']],
    ACE: ['Poseidon trident wild', '🔱', symbols.ACE], FREE: ['Pearl cavern free game', '🦪', symbols.FREE], POWER: ['Royal ocean pearl', '💎', symbols.POWER],
    BOLT: ['Tidal wave charge', '🌊', symbols.BOLT], BANANA: ['Triple coral branches', '🪸', symbols.BANANA], PAW: ['Pearl-diver net', '🕸️', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 28, collectionAwardedSpins: 6,
    freeGames: { requiredSymbols: 4, awardedSpins: 6 }, sounds: OCEAN_ODYSSEY_SOUNDS,
    usesDirectValueTokens: false,
    feature: {
      id: 'ocean-pearl-voyage', title: 'Pearl Voyage',
      earnLabel: 'Land 4 pearl caverns for 6 Pearl Voyages, or complete 28 treasures for 6 enhanced dives.',
      earnStyle: 'buckets', earnHint: 'Coral treasures settle into diver nets; fill a net to earn an enhanced Pearl Voyage.',
    activeModes: {
      sync: 'Current Link · a matching reel is copied', rows: 'Coral Rise · two extra rows',
      paw: 'Diver’s Net · extra wilds appear', rand: 'Pearl Bed · a wild reel appears',
      'sync-rand': 'Pearl Current · linked reels with a wild lane',
    },
    },
  },
  colors: { skyTop: '#041d45', skyBottom: '#0877a5', horizon: '#075d78', ground: '#031629', primary: '#0a87b8', secondary: '#45e0cf', deep: '#03253b', rim: '#ffe18a', glow: '#69eaff', text: '#ecffff' },
}

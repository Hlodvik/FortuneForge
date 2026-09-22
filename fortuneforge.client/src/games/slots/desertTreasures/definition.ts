import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { DESERT_TREASURES_SOUNDS } from '../../../features/slots/config/soundSets'
import { DESERT_TREASURES_SYMBOL_IMAGES as symbols } from './symbols'

export const DESERT_TREASURES_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'desert-treasures', title: 'Desert Treasures', subtitle: 'Awaken the Golden Tomb',
  description: 'Follow the desert stars past ancient scarabs, hidden oases, and a sealed pharaoh’s vault.',
  serverGameId: 'desert-treasures-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 20, 21, 22, 23],
  presentation: 'frontier-trail', collectionAriaLabel: 'Royal scarab collections', itemLabel: 'tomb relics',
  energyLabel: 'Sun-disc charge', actorName: 'The royal excavation satchel', awardLabel: 'Pharaoh vault haul',
  outcomeNarrative: {
    lossTitle: 'No tomb treasure this spin', winTitle: 'Tomb treasure', greatWinTitle: 'Golden discovery', bigWinTitle: "Pharaoh's jackpot",
    freeGameSingular: 'free excavation', freeGamePlural: 'free excavations',
    lossNextAction: 'Choose a wager, then explore the tomb again.',
    winNextAction: 'Choose a wager, then search for another golden discovery.',
    bonusNextAction: 'Your next excavation is free and uses the locked wager.',
  },
  celebrationEffect: 'desert-sandstorm',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['ancient gold scarab', '🪲', symbols.VALUE], motif: '🔺', accentGlyph: '☀️',
  collectionLabels: [['Crimson ankh relic', '🔻', symbols.SEAL_SYNC], ['Sapphire lotus relic', '🪷', symbols.SEAL_ROWS], ['Amber sun relic', '☀️', symbols.SEAL_PAW], ['Emerald scarab relic', '🪲', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Painted clay urn', '🏺', symbols['2']], '3': ['Jeweled scarab', '🪲', symbols['3']], '4': ['Desert camel', '🐪', symbols['4']],
    '5': ['Hidden oasis palm', '🌴', symbols['5']], '6': ['Guardian sphinx', '🦁', symbols['6']], '7': ['Great golden pyramid', '🔺', symbols['7']],
    ACE: ['Pharaoh crown wild', '👑', symbols.ACE], FREE: ['Sealed tomb free game', '🚪', symbols.FREE], POWER: ['Radiant sun disc', '☀️', symbols.POWER],
    BOLT: ['Sandstorm charge', '🌪️', symbols.BOLT], BANANA: ['Triple golden ankhs', '☥', symbols.BANANA], PAW: ['Royal excavation satchel', '🎒', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 24, collectionAwardedSpins: 7,
    freeGames: { requiredSymbols: 3, awardedSpins: 7 }, sounds: DESERT_TREASURES_SOUNDS,
    feature: {
      id: 'desert-pharaoh-passage', title: 'Pharaoh’s Passage',
      earnLabel: 'Land 3 sealed tombs for 7 Passages, or complete 24 relics for 7 enhanced passages.',
      earnStyle: 'trail', earnHint: 'Tomb relics reveal a desert route; complete one route to open Pharaoh’s Passage.',
      activeModes: {
        sync: 'Ankh Echo · a matching reel is copied', rows: 'Tomb Unsealed · two extra rows',
        paw: 'Excavation Crew · collectors appear more often', rand: 'Pharaoh’s Vault · a prize reel appears',
      },
    },
  },
  colors: { skyTop: '#4f2610', skyBottom: '#d7862f', horizon: '#9d5e24', ground: '#2a160c', primary: '#b97822', secondary: '#29a9a0', deep: '#3b210f', rim: '#ffe18a', glow: '#ffbd47', text: '#fff1cf' },
}

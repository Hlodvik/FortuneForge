import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { ROBOT_REVOLUTION_SOUNDS } from '../../../features/slots/config/soundSets'
import { ROBOT_REVOLUTION_SYMBOL_IMAGES as symbols } from './symbols'

export const ROBOT_REVOLUTION_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'robot-revolution', title: 'Robot Revolution', subtitle: 'Power Up the Future',
  description: 'Charge a chrome megacity of clever drones, fusion cores, and jackpot-building machines.',
  serverGameId: 'robot-revolution-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21, 22, 23],
  presentation: 'chip-stack', collectionAriaLabel: 'Circuit module collections', itemLabel: 'data modules',
  energyLabel: 'Fusion charge', actorName: 'The quantum magnet array', awardLabel: 'Megacity data haul',
  outcomeNarrative: {
    lossTitle: 'No charge this spin', winTitle: 'Power-up won', greatWinTitle: 'Mega charge', bigWinTitle: 'Robot jackpot',
    freeGameSingular: 'free boost', freeGamePlural: 'free boosts',
    lossNextAction: 'Choose a wager, then power up the city again.',
    winNextAction: 'Choose a wager, then build another mega charge.',
    bonusNextAction: 'Your next boost is free and uses the locked wager.',
  },
  celebrationEffect: 'robot-circuit',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['quantum data chip', '💾', symbols.VALUE], motif: '🤖', accentGlyph: '⚙️',
  collectionLabels: [['Crimson combat module', '🔴', symbols.SEAL_SYNC], ['Sapphire logic module', '🔷', symbols.SEAL_ROWS], ['Amber power module', '🔶', symbols.SEAL_PAW], ['Emerald repair module', '🟢', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Precision gear', '⚙️', symbols['2']], '3': ['Fusion battery', '🔋', symbols['3']], '4': ['Quantum microchip', '💾', symbols['4']],
    '5': ['Courier drone', '🚁', symbols['5']], '6': ['Service android', '🤖', symbols['6']], '7': ['Titan construction mech', '🦾', symbols['7']],
    ACE: ['Sentient AI core wild', '🧠', symbols.ACE], FREE: ['Quantum portal free game', '🌀', symbols.FREE], POWER: ['Fusion reactor power', '🔵', symbols.POWER],
    BOLT: ['Electric grid charge', '⚡', symbols.BOLT], BANANA: ['Triple golden gears', '⚙️', symbols.BANANA], PAW: ['Quantum magnet array', '🧲', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 24, collectionAwardedSpins: 8,
    freeGames: { requiredSymbols: 3, awardedSpins: 7 }, sounds: ROBOT_REVOLUTION_SOUNDS,
    usesCollections: false, usesEnergy: false, usesDirectValueTokens: false,
    earnHelp: 'Land 3 quantum portals anywhere in a spin to start seven Assembly Runs with an extra pair of rows.',
    feature: {
      id: 'robot-overclock', title: 'Overclock Run',
      earnLabel: 'Land 3 quantum portals for 7 Overclock Runs.',
      earnStyle: 'gates', earnHint: 'Quantum portals light one gate at a time; three in a spin opens an Overclock Run.',
      activeModes: {
        sync: 'Linked Processors · a matching reel is copied', rows: 'Assembly Line · two extra rows',
        paw: 'Magnet Surge · extra wilds appear', rand: 'Data Flood · a wild reel appears',
      },
    },
  },
  colors: { skyTop: '#07142d', skyBottom: '#174d70', horizon: '#163951', ground: '#040c18', primary: '#237fa7', secondary: '#34e0b8', deep: '#092138', rim: '#ffd54c', glow: '#37ddff', text: '#effcff' },
}

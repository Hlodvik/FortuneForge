import type { ShowcaseSlotGameDefinition } from '@fortuneforge/slot-game-runtime/showcase'
import { SUNSTONE_ECLIPSE_SOUNDS } from './sounds'
import { SUNSTONE_ECLIPSE_SYMBOL_IMAGES as symbols } from './symbols'

export const SUNSTONE_ECLIPSE_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'robot-revolution', title: 'Sunstone Eclipse', subtitle: 'Temple of the Fifth Sun',
  description: 'Turn three carved guardian rings, awaken Jaguar, Eagle and Serpent, and align the central Sun for an Eclipse tribute.',
  serverGameId: 'robot-revolution-v1',
  initialReels: [
    ['2', '3', '4', '5'], ['3', '5', '6', '7'], ['5', '6', 'POWER', '2'],
    ['6', '7', 'ACE', '3'], ['FREE', '2', '3', '4'],
  ],
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21, 22, 23],
  paylineTrace: { color: '#fbe4a0', glowColor: '#28cbb0' },
  presentation: 'divine-offering', collectionAriaLabel: 'Guardian rings', itemLabel: 'sunstone glyphs',
  energyLabel: 'Sunlight', actorName: 'The Sun Calendar', awardLabel: 'Temple tribute',
  outcomeNarrative: {
    lossTitle: 'The temple rests', winTitle: 'Temple treasure', greatWinTitle: 'A guardian awakens', bigWinTitle: 'The Fifth Sun rises',
    freeGameSingular: 'Eclipse spin', freeGamePlural: 'Eclipse spins',
    lossNextAction: 'Choose a wager, then turn the calendar when ready.',
    winNextAction: 'Your treasure is settled. Turn the calendar when ready.',
    bonusNextAction: 'Your next Eclipse spin is free and uses the saved wager.',
  },
  celebrationEffect: 'sunstone-corona',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['Turquoise tribute', '◆', symbols.VALUE], motif: '☀', accentGlyph: '◆',
  collectionLabels: [['Jaguar guardian', '◆', symbols.SEAL_SYNC], ['Eagle guardian', '◆', symbols.SEAL_ROWS], ['Serpent guardian', '◆', symbols.SEAL_PAW], ['Golden Sunstone', '☀', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Cacao offering', '◆', symbols['2']], '3': ['Quetzal feather', '◆', symbols['3']], '4': ['Turquoise earspool', '◆', symbols['4']],
    '5': ['Feathered Serpent', '◆', symbols['5']], '6': ['Eagle guardian', '◆', symbols['6']], '7': ['Jaguar mask', '◆', symbols['7']],
    ACE: ['Sun Calendar Wild', '☀', symbols.ACE], FREE: ['Eclipse Scatter', '◉', symbols.FREE], POWER: ['Golden Sunstone', '☀', symbols.POWER],
    BOLT: ['Sunray', '☀', symbols.BOLT], BANANA: ['Triple Sunstone', '☀', symbols.BANANA], PAW: ['Temple seal', '◆', symbols.PAW],
  },
  helpSections: [
    { badge: 'I', title: 'Turn the Sunwheel', body: 'Golden Sunstones determine a new alignment of three guardian paths on every settled board. The Jaguar, Eagle and Serpent each follow their own five-position path.' },
    { badge: 'II', title: 'Awaken the guardians', body: 'On its path, a guardian matches its own symbol, Golden Sunstones and Wilds. Three, four or five matches pay 0.5×, 1× or 2× the wager. Matches do not need to touch. Two aligned guardians multiply every guardian award by two; all three multiply every award by three. Each guardian award rounds down to whole wager points, worth 25 cents each.' },
    { badge: 'III', title: 'The Eclipse tribute', body: 'Align all three guardians and land a Golden Sunstone at the heart of reel three for a further 2× the wager. The heart is the lower central cell: row three on the base board, or row four during Eclipse spins. Sunwheel awards are added to ordinary wins.' },
    { badge: '7', title: 'Eclipse spins', body: 'Three Eclipse scatters award seven free Eclipse spins. The reels expand by two rows and every Sunwheel award doubles, including the Eclipse tribute. The triggering wager stays locked until the bonus finishes.' },
  ],
  specialRound: {
    collectionTarget: 24, collectionAwardedSpins: 8,
    freeGames: { requiredSymbols: 3, awardedSpins: 7, title: 'Eclipse spins', symbolLabel: 'ECLIPSE', awardLabel: 'free Eclipse spins' },
    sounds: SUNSTONE_ECLIPSE_SOUNDS,
    usesCollections: false, usesEnergy: false, usesDirectValueTokens: false,
    earnHelp: 'Three Eclipse scatters award seven free Eclipse spins with two extra rows and doubled Sunwheel awards.',
    feature: {
      id: 'sunstone-eclipse', title: 'Eclipse spins',
      earnLabel: 'Land 3 Eclipse scatters for 7 free Eclipse spins.',
      earnStyle: 'altar', earnHint: 'Two extra rows open and Sunwheel awards double during the free round.',
      activeModes: {
        rows: 'Saved bonus spins · two extra rows',
        'rows-arcade': 'Eclipse spins · two extra rows · Sunwheel awards ×2',
      },
    },
  },
  colors: { skyTop: '#130e15', skyBottom: '#503321', horizon: '#9b6731', ground: '#101c1a', primary: '#354a3e', secondary: '#56d5bd', deep: '#0b1414', rim: '#c69a4c', glow: '#f1ce7f', text: '#fff5dc' },
}

export const ROBOT_REVOLUTION_DEFINITION = SUNSTONE_ECLIPSE_DEFINITION

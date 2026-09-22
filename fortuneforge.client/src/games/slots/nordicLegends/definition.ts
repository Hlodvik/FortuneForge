import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { NORDIC_LEGENDS_SOUNDS } from '../../../features/slots/config/soundSets'
import { NORDIC_LEGENDS_SYMBOL_IMAGES as symbols } from './symbols'

export const NORDIC_LEGENDS_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'nordic-legends', title: 'Nordic Legends', subtitle: 'Claim the Halls of Valor',
  description: 'Sail beneath the aurora toward rune-carved peaks, thunder gods, and a warrior’s hoard.',
  serverGameId: 'nordic-legends-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 22, 23],
  presentation: 'divine-offering', collectionAriaLabel: 'Runestone offering collections', itemLabel: 'sacred runes',
  energyLabel: 'Thunder charge', actorName: 'The Valkyrie treasure chest', awardLabel: 'Valhalla war chest',
  outcomeNarrative: {
    lossTitle: 'No rune reward this spin', winTitle: 'Viking reward', greatWinTitle: 'Legendary boon', bigWinTitle: 'Valhalla jackpot',
    freeGameSingular: 'free raid', freeGamePlural: 'free raids',
    lossNextAction: 'Choose a wager, then sail for Valhalla again.',
    winNextAction: 'Choose a wager, then claim another warrior reward.',
    bonusNextAction: 'Your next raid is free and uses the locked wager.',
  },
  celebrationEffect: 'nordic-aurora',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['stamped silver rune', '🪙', symbols.VALUE], motif: '⚔️', accentGlyph: '❄️',
  collectionLabels: [['Crimson wolf rune', '🐺', symbols.SEAL_SYNC], ['Sapphire wave rune', '🌊', symbols.SEAL_ROWS], ['Amber hammer rune', '🔨', symbols.SEAL_PAW], ['Emerald world-tree rune', '🌲', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Carved runestone', '🪨', symbols['2']], '3': ['Feast-hall drinking horn', '🍺', symbols['3']], '4': ['Viking round shield', '🛡️', symbols['4']],
    '5': ['Battle axe', '🪓', symbols['5']], '6': ['Dragon-prow longship', '⛵', symbols['6']], '7': ['Winged Valkyrie', '🪽', symbols['7']],
    ACE: ['All-seeing Odin wild', '👁️', symbols.ACE], FREE: ['Bifrost bridge free game', '🌈', symbols.FREE], POWER: ['Mjolnir thunder power', '🔨', symbols.POWER],
    BOLT: ['Aurora lightning charge', '⚡', symbols.BOLT], BANANA: ['Triple battle axes', '🪓', symbols.BANANA], PAW: ['Valkyrie treasure chest', '🧰', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 28, collectionAwardedSpins: 6,
    freeGames: { requiredSymbols: 4, awardedSpins: 6 }, sounds: NORDIC_LEGENDS_SOUNDS,
    usesCollections: false, usesEnergy: false,
    earnHelp: 'Land 4 Bifrost bridges anywhere in a spin to start six Valhalla Voyages with linked reels.',
    feature: {
      id: 'nordic-valhalla-voyage', title: 'Valhalla Voyage',
      earnLabel: 'Land 4 Bifrost bridges for 6 Valhalla Voyages.',
      earnStyle: 'altar', earnHint: 'Bifrost shards restore the rainbow bridge; all four shards start a Valhalla Voyage.',
    activeModes: {
      sync: 'Raven’s Sight · a matching reel is copied', rows: 'Thunder Hall · two extra rows',
      paw: 'Valkyrie’s Chest · collectors appear more often', rand: 'Odin’s Hoard · a prize reel appears',
      'sync-paw': 'Valkyrie’s Flight · linked reels with a wild burst',
    },
    },
  },
  colors: { skyTop: '#102d46', skyBottom: '#3b6a78', horizon: '#274858', ground: '#091a27', primary: '#477b8e', secondary: '#80dfca', deep: '#102535', rim: '#d5b766', glow: '#8fffe2', text: '#f0fbff' },
}

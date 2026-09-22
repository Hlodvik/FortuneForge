import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { DRAGON_HOARD_SOUNDS } from '../../../features/slots/config/soundSets'
import { DRAGON_HOARD_SYMBOL_IMAGES as symbols } from './symbols'

export const DRAGON_HOARD_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'dragon-hoard', title: 'Dragon Hoard', subtitle: 'Enter the Ember Vault',
  description: 'Storm a firelit mountain keep where knights, royal relics, and a great dragon guard the gold.',
  serverGameId: 'dragon-hoard-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 19, 20, 21, 22, 23],
  presentation: 'gem-hoard', collectionAriaLabel: 'Dragon gem collections', itemLabel: 'royal gemstones',
  energyLabel: 'Dragonfire charge', actorName: 'The enchanted treasure chest', awardLabel: 'Ember-vault hoard',
  outcomeNarrative: {
    lossTitle: 'No hoard found this spin', winTitle: 'Hoard claimed', greatWinTitle: 'Ember reward', bigWinTitle: 'Dragon jackpot',
    freeGameSingular: 'free flight', freeGamePlural: 'free flights',
    lossNextAction: 'Choose a wager, then enter the ember vault again.',
    winNextAction: 'Choose a wager, then seek another dragon hoard.',
    bonusNextAction: 'Your next flight is free and uses the locked wager.',
  },
  celebrationEffect: 'dragon-embers',
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP },
  valueToken: ['dragon-stamped gold coin', '🪙', symbols.VALUE], motif: '🐉', accentGlyph: '🔥',
  collectionLabels: [['Crimson fire gem', '🔴', symbols.SEAL_SYNC], ['Sapphire frost gem', '🔷', symbols.SEAL_ROWS], ['Amber sun gem', '🔶', symbols.SEAL_PAW], ['Emerald earth gem', '🟢', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Royal gold coin', '🪙', symbols['2']], '3': ['Jeweled goblet', '🏆', symbols['3']], '4': ['Knight sword', '⚔️', symbols['4']],
    '5': ['Royal tower shield', '🛡️', symbols['5']], '6': ['Charging knight', '🏇', symbols['6']], '7': ['Mountain king castle', '🏰', symbols['7']],
    ACE: ['Ancient dragon wild', '🐉', symbols.ACE], FREE: ['Ember lair free game', '🕳️', symbols.FREE], POWER: ['Royal ruby power', '💎', symbols.POWER],
    BOLT: ['Dragonfire charge', '🔥', symbols.BOLT], BANANA: ['Triple dragon claws', '🐾', symbols.BANANA], PAW: ['Enchanted treasure chest', '🧰', symbols.PAW],
  },
  specialRound: {
    collectionTarget: 30, collectionAwardedSpins: 7,
    freeGames: { requiredSymbols: 3, awardedSpins: 6 }, sounds: DRAGON_HOARD_SOUNDS,
    usesEnergy: false, usesDirectValueTokens: false,
    feature: {
      id: 'dragon-ember-siege', title: 'Ember Siege',
      earnLabel: 'Land 3 ember lairs for 6 Ember Sieges, or complete 30 gems for 7 enhanced sieges.',
      earnStyle: 'buckets', earnHint: 'Royal gemstones pile into ember vaults; complete a vault to begin an Ember Siege.',
    activeModes: {
      sync: 'Dragon’s Echo · a matching reel is copied', rows: 'Castle Breach · two extra rows',
      paw: 'Hoard Hunter · extra wilds appear', rand: 'Vault of Gold · a wild reel appears',
      'rows-paw': 'Castle Breach · two extra rows with extra wilds',
    },
    },
  },
  colors: { skyTop: '#2f0a0a', skyBottom: '#81251c', horizon: '#4f1a13', ground: '#160706', primary: '#9b2b1e', secondary: '#e99b31', deep: '#300d09', rim: '#f2ce67', glow: '#ff6b32', text: '#fff0d3' },
}

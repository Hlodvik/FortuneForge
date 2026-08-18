import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const DRAGON_HOARD_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'dragon-hoard', title: 'Dragon Hoard', subtitle: 'Enter the Ember Vault',
  description: 'Storm a firelit mountain keep where knights, royal relics, and a great dragon guard the gold.',
  serverGameId: 'dragon-hoard-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 19, 20, 21, 22, 23],
  presentation: 'gem-hoard', collectionAriaLabel: 'Dragon gem collections', itemLabel: 'royal gemstones',
  energyLabel: 'Dragonfire charge', actorName: 'The enchanted treasure chest', awardLabel: 'Ember-vault hoard',
  valueToken: ['dragon-stamped gold coin', '🪙'], motif: '🐉', accentGlyph: '🔥',
  collectionLabels: [['Crimson fire gem', '🔴'], ['Sapphire frost gem', '🔷'], ['Amber sun gem', '🔶'], ['Emerald earth gem', '🟢']],
  symbolSpecs: {
    '2': ['Royal gold coin', '🪙'], '3': ['Jeweled goblet', '🏆'], '4': ['Knight sword', '⚔️'],
    '5': ['Royal tower shield', '🛡️'], '6': ['Charging knight', '🏇'], '7': ['Mountain king castle', '🏰'],
    ACE: ['Ancient dragon wild', '🐉'], FREE: ['Ember lair free game', '🕳️'], POWER: ['Royal ruby power', '💎'],
    BOLT: ['Dragonfire charge', '🔥'], BANANA: ['Triple dragon claws', '🐾'], PAW: ['Enchanted treasure chest', '🧰'],
  },
  colors: { skyTop: '#2f0a0a', skyBottom: '#81251c', horizon: '#4f1a13', ground: '#160706', primary: '#9b2b1e', secondary: '#e99b31', deep: '#300d09', rim: '#f2ce67', glow: '#ff6b32', text: '#fff0d3' },
}

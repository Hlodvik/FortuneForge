import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const PHANTOM_MANOR_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'phantom-manor', title: 'Phantom Manor', subtitle: 'Fortune Haunts These Halls',
  description: 'Unlock a moonlit estate of whispering portraits, restless ravens, and spectral treasure.',
  serverGameId: 'phantom-manor-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 18, 19, 20, 21, 22, 23],
  presentation: 'spellbook-shelf', collectionAriaLabel: 'Haunted portrait collections', itemLabel: 'spirit seals',
  energyLabel: 'Moonlight charge', actorName: 'The séance spirit lantern', awardLabel: 'Phantom treasure haul',
  valueToken: ['captured spirit wisp', '👻'], motif: '🏚️', accentGlyph: '🌙',
  collectionLabels: [['Crimson raven seal', '🐦‍⬛'], ['Sapphire mirror seal', '🪞'], ['Amber candle seal', '🕯️'], ['Emerald key seal', '🗝️']],
  symbolSpecs: {
    '2': ['Whispering candle', '🕯️'], '3': ['Ancient skeleton key', '🗝️'], '4': ['Midnight raven', '🐦‍⬛'],
    '5': ['Haunted mirror', '🪞'], '6': ['Restless phantom', '👻'], '7': ['Moonlit manor', '🏚️'],
    ACE: ['Jeweled skull wild', '💀'], FREE: ['Forbidden crypt free game', '🚪'], POWER: ['Séance crystal power', '🔮'],
    BOLT: ['Full-moon charge', '🌙'], BANANA: ['Triple shadow bats', '🦇'], PAW: ['Séance spirit lantern', '🏮'],
  },
  colors: { skyTop: '#0d1028', skyBottom: '#332550', horizon: '#20203c', ground: '#080913', primary: '#5b4a8d', secondary: '#73d0ba', deep: '#151329', rim: '#c8b47a', glow: '#a58cff', text: '#f4f0ff' },
}

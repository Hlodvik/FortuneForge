import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const CANDY_CARNIVAL_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'candy-carnival', title: 'Candy Carnival', subtitle: 'The Sweetest Show on Reels',
  description: 'Step under the striped big top for spun sugar, candy rides, and a sparkling prize parade.',
  serverGameId: 'candy-carnival-v1',
  paylinePatternIds: Array.from({ length: 22 }, (_, index) => index + 1),
  presentation: 'juice-glass', collectionAriaLabel: 'Candy jar collections', itemLabel: 'candy charms',
  energyLabel: 'Sugar-rush charge', actorName: 'The carnival candy bag', awardLabel: 'Sweet-shop haul',
  valueToken: ['rainbow candy token', '🍬'], motif: '🎪', accentGlyph: '🍭',
  collectionLabels: [['Strawberry heart charm', '💗'], ['Blueberry drop charm', '🔵'], ['Caramel star charm', '⭐'], ['Lime swirl charm', '🟢']],
  symbolSpecs: {
    '2': ['Wrapped fruit candy', '🍬'], '3': ['Rainbow lollipop', '🍭'], '4': ['Frosted cupcake', '🧁'],
    '5': ['Sprinkle doughnut', '🍩'], '6': ['Cotton-candy cone', '🍡'], '7': ['Golden candy carousel', '🎠'],
    ACE: ['Candy crown wild', '👑'], FREE: ['Big-top free game', '🎪'], POWER: ['Giant jawbreaker power', '🔴'],
    BOLT: ['Sugar sparkle charge', '✨'], BANANA: ['Triple candy canes', '🍭'], PAW: ['Carnival candy bag', '🛍️'],
  },
  colors: { skyTop: '#4a1a62', skyBottom: '#e2599a', horizon: '#8f3e89', ground: '#2a103a', primary: '#ff5eaa', secondary: '#60e2e8', deep: '#351047', rim: '#ffe26d', glow: '#ffb8e5', text: '#fff5fb' },
}

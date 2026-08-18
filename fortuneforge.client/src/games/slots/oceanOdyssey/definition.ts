import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const OCEAN_ODYSSEY_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'ocean-odyssey', title: 'Ocean Odyssey', subtitle: 'Dive Beyond the Blue',
  description: 'Descend through coral gardens and ancient currents in search of a luminous pearl kingdom.',
  serverGameId: 'ocean-odyssey-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 23],
  presentation: 'gem-hoard', collectionAriaLabel: 'Coral treasure collections', itemLabel: 'sea treasures',
  energyLabel: 'Tidal charge', actorName: 'The pearl-diver net', awardLabel: 'Deep-sea treasure haul',
  valueToken: ['luminous pearl', '🫧'], motif: '🌊', accentGlyph: '🐚',
  collectionLabels: [['Crimson coral treasure', '🪸'], ['Sapphire shell treasure', '🐚'], ['Amber star treasure', '⭐'], ['Emerald turtle treasure', '🐢']],
  symbolSpecs: {
    '2': ['Spiral seashell', '🐚'], '3': ['Golden starfish', '⭐'], '4': ['Striped tropical fish', '🐠'],
    '5': ['Playful dolphin', '🐬'], '6': ['Ancient sea turtle', '🐢'], '7': ['Great blue whale', '🐋'],
    ACE: ['Poseidon trident wild', '🔱'], FREE: ['Pearl cavern free game', '🦪'], POWER: ['Royal ocean pearl', '💎'],
    BOLT: ['Tidal wave charge', '🌊'], BANANA: ['Triple coral branches', '🪸'], PAW: ['Pearl-diver net', '🕸️'],
  },
  colors: { skyTop: '#041d45', skyBottom: '#0877a5', horizon: '#075d78', ground: '#031629', primary: '#0a87b8', secondary: '#45e0cf', deep: '#03253b', rim: '#ffe18a', glow: '#69eaff', text: '#ecffff' },
}

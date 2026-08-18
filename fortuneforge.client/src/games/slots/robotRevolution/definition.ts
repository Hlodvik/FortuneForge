import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const ROBOT_REVOLUTION_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'robot-revolution', title: 'Robot Revolution', subtitle: 'Power Up the Future',
  description: 'Charge a chrome megacity of clever drones, fusion cores, and jackpot-building machines.',
  serverGameId: 'robot-revolution-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21, 22, 23],
  presentation: 'chip-stack', collectionAriaLabel: 'Circuit module collections', itemLabel: 'data modules',
  energyLabel: 'Fusion charge', actorName: 'The quantum magnet array', awardLabel: 'Megacity data haul',
  valueToken: ['quantum data chip', '💾'], motif: '🤖', accentGlyph: '⚙️',
  collectionLabels: [['Crimson combat module', '🔴'], ['Sapphire logic module', '🔷'], ['Amber power module', '🔶'], ['Emerald repair module', '🟢']],
  symbolSpecs: {
    '2': ['Precision gear', '⚙️'], '3': ['Fusion battery', '🔋'], '4': ['Quantum microchip', '💾'],
    '5': ['Courier drone', '🚁'], '6': ['Service android', '🤖'], '7': ['Titan construction mech', '🦾'],
    ACE: ['Sentient AI core wild', '🧠'], FREE: ['Quantum portal free game', '🌀'], POWER: ['Fusion reactor power', '🔵'],
    BOLT: ['Electric grid charge', '⚡'], BANANA: ['Triple golden gears', '⚙️'], PAW: ['Quantum magnet array', '🧲'],
  },
  colors: { skyTop: '#07142d', skyBottom: '#174d70', horizon: '#163951', ground: '#040c18', primary: '#237fa7', secondary: '#34e0b8', deep: '#092138', rim: '#ffd54c', glow: '#37ddff', text: '#effcff' },
}

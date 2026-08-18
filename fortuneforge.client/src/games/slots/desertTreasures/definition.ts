import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const DESERT_TREASURES_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'desert-treasures', title: 'Desert Treasures', subtitle: 'Awaken the Golden Tomb',
  description: 'Follow the desert stars past ancient scarabs, hidden oases, and a sealed pharaoh’s vault.',
  serverGameId: 'desert-treasures-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 20, 21, 22, 23],
  presentation: 'frontier-trail', collectionAriaLabel: 'Royal scarab collections', itemLabel: 'tomb relics',
  energyLabel: 'Sun-disc charge', actorName: 'The royal excavation satchel', awardLabel: 'Pharaoh vault haul',
  valueToken: ['ancient gold scarab', '🪲'], motif: '🔺', accentGlyph: '☀️',
  collectionLabels: [['Crimson ankh relic', '🔻'], ['Sapphire lotus relic', '🪷'], ['Amber sun relic', '☀️'], ['Emerald scarab relic', '🪲']],
  symbolSpecs: {
    '2': ['Painted clay urn', '🏺'], '3': ['Jeweled scarab', '🪲'], '4': ['Desert camel', '🐪'],
    '5': ['Hidden oasis palm', '🌴'], '6': ['Guardian sphinx', '🦁'], '7': ['Great golden pyramid', '🔺'],
    ACE: ['Pharaoh crown wild', '👑'], FREE: ['Sealed tomb free game', '🚪'], POWER: ['Radiant sun disc', '☀️'],
    BOLT: ['Sandstorm charge', '🌪️'], BANANA: ['Triple golden ankhs', '☥'], PAW: ['Royal excavation satchel', '🎒'],
  },
  colors: { skyTop: '#4f2610', skyBottom: '#d7862f', horizon: '#9d5e24', ground: '#2a160c', primary: '#b97822', secondary: '#29a9a0', deep: '#3b210f', rim: '#ffe18a', glow: '#ffbd47', text: '#fff1cf' },
}

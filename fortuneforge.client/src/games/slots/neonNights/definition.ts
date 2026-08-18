import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const NEON_NIGHTS_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'neon-nights',
  title: 'Neon Nights',
  subtitle: 'Light Up the Midnight Jackpot',
  description: 'Race through an electric city of arcade lights, synth beats, and glowing midnight prizes.',
  serverGameId: 'neon-nights-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 17, 18, 19, 20, 21, 22, 23],
  presentation: 'star-orbit',
  collectionAriaLabel: 'Neon district collections',
  itemLabel: 'neon badges',
  energyLabel: 'Voltage charge',
  actorName: 'The midnight DJ deck',
  awardLabel: 'Neon remix haul',
  valueToken: ['glowing arcade chip', '💿'],
  motif: '🌃',
  accentGlyph: '🎵',
  collectionLabels: [['Pink heart badge', '💗'], ['Blue diamond badge', '💎'], ['Orange star badge', '🌟'], ['Green music badge', '🎵']],
  symbolSpecs: {
    '2': ['Neon cherry', '🍒'], '3': ['Retro cassette', '📼'], '4': ['Electric roller skate', '🛼'],
    '5': ['Arcade joystick', '🕹️'], '6': ['Midnight sports car', '🏎️'], '7': ['Neon skyline', '🌃'],
    ACE: ['Diamond shades wild', '🕶️'], FREE: ['Nightclub doorway free game', '🚪'], POWER: ['Electric star power', '🌟'],
    BOLT: ['Voltage lightning', '⚡'], BANANA: ['Triple neon sevens', '7️⃣'], PAW: ['Midnight DJ deck', '🎛️'],
  },
  colors: { skyTop: '#070b2d', skyBottom: '#34105c', horizon: '#091c43', ground: '#050819', primary: '#cf2cff', secondary: '#22e7ff', deep: '#16072e', rim: '#ffd34d', glow: '#ff4fd8', text: '#fff4ff' },
}

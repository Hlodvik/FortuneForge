import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const SAMURAI_FORTUNE_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'samurai-fortune', title: 'Samurai Fortune', subtitle: 'Honor Meets the Rising Sun',
  description: 'Cross a lantern-lit province of blossom gardens, guarded castles, and legendary blades.',
  serverGameId: 'samurai-fortune-v1',
  paylinePatternIds: Array.from({ length: 23 }, (_, index) => index + 1),
  presentation: 'seal-pile', collectionAriaLabel: 'Clan crest collections', itemLabel: 'clan crests',
  energyLabel: 'Spirit charge', actorName: 'The shogun treasure satchel', awardLabel: 'Clan treasury haul',
  valueToken: ['golden mon coin', '🪙'], motif: '⛩️', accentGlyph: '🌸',
  collectionLabels: [['Crimson crane crest', '🦢'], ['Sapphire moon crest', '🌙'], ['Amber sun crest', '☀️'], ['Jade dragon crest', '🐉']],
  symbolSpecs: {
    '2': ['Ceremonial rice bowl', '🍚'], '3': ['Painted folding fan', '🪭'], '4': ['Festival lantern', '🏮'],
    '5': ['Cherry blossom', '🌸'], '6': ['Forged katana', '🗡️'], '7': ['Mountain shogun castle', '🏯'],
    ACE: ['Dragon mask wild', '🐉'], FREE: ['Sacred torii free game', '⛩️'], POWER: ['Jade spirit orb', '🟢'],
    BOLT: ['Storm spirit charge', '⚡'], BANANA: ['Triple shuriken', '🥷'], PAW: ['Shogun treasure satchel', '🎒'],
  },
  colors: { skyTop: '#341018', skyBottom: '#9d3b45', horizon: '#492023', ground: '#180b0c', primary: '#a51f2d', secondary: '#f0b8bb', deep: '#2b0b10', rim: '#e6bd63', glow: '#ff8a94', text: '#fff1e7' },
}

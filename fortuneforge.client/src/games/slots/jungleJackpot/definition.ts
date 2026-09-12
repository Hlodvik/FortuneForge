import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'

export const JUNGLE_JACKPOT_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'jungle-jackpot', title: 'Jungle Jackpot', subtitle: 'Find the Golden Temple',
  description: 'Push through a living rainforest where bright wildlife guards an overgrown golden temple.',
  serverGameId: 'jungle-jackpot-v1',
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 16, 17, 18, 19, 20, 21, 22, 23],
  presentation: 'fossil-dig', collectionAriaLabel: 'Rainforest relic collections', itemLabel: 'jungle relics',
  energyLabel: 'Sunbeam charge', actorName: 'The explorer field pack', awardLabel: 'Temple expedition haul',
  outcomeNarrative: {
    lossTitle: 'No temple haul this spin', winTitle: 'Temple haul', greatWinTitle: 'Golden discovery', bigWinTitle: 'Jungle jackpot',
    freeGameSingular: 'free expedition', freeGamePlural: 'free expeditions',
    lossNextAction: 'Choose a wager, then explore the jungle again.',
    winNextAction: 'Choose a wager, then set out for another temple haul.',
    bonusNextAction: 'Your next expedition is free and uses the locked wager.',
  },
  celebrationEffect: 'jungle-canopy',
  valueToken: ['golden temple coin', '🪙'], motif: '🌴', accentGlyph: '🦜',
  collectionLabels: [['Jaguar fang relic', '🐆'], ['Blue orchid relic', '🪻'], ['Amber sun relic', '☀️'], ['Emerald frog relic', '🐸']],
  symbolSpecs: {
    '2': ['Tropical leaf', '🍃'], '3': ['Scarlet parrot', '🦜'], '4': ['Tree frog', '🐸'],
    '5': ['Playful monkey', '🐒'], '6': ['Rainforest jaguar', '🐆'], '7': ['Golden jungle temple', '🛕'],
    ACE: ['Golden tiger wild', '🐅'], FREE: ['Hidden waterfall free game', '🏞️'], POWER: ['Radiant sun idol', '☀️'],
    BOLT: ['Firefly charge', '✨'], BANANA: ['Triple jungle vines', '🌿'], PAW: ['Explorer field pack', '🎒'],
  },
  colors: { skyTop: '#082f2b', skyBottom: '#1f6b43', horizon: '#17492d', ground: '#071d15', primary: '#3a8f3b', secondary: '#f2b94b', deep: '#092719', rim: '#ffe07a', glow: '#8be34f', text: '#f4ffe6' },
}

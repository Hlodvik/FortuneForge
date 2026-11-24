import { CROOKED_ROAD_GAME_ID, CROOKED_ROAD_SYMBOL_SET_ID, CROOKED_ROAD_RULES as ruleContract } from './types'
import { CROOKED_ROAD_ARTWORK as artwork, CROOKED_ROAD_SYMBOL_IMAGES as symbols } from './symbols'

export const CROOKED_ROAD_RULE_CONTRACT = ruleContract
const formatPayout = (units: number) => `${Number((units * ruleContract.payoutScaleNumerator /
  (ruleContract.payoutUnits * ruleContract.payoutScaleDenominator)).toFixed(6))}×`
const payingSymbolGuide = ruleContract.symbols.filter(symbol => symbol.pays.length === 3).map(symbol => ({
  symbol: symbol.id, firstLabel: '3 / 4', firstValue: `${formatPayout(symbol.pays[0])} / ${formatPayout(symbol.pays[1])}`,
  secondLabel: '5', secondValue: formatPayout(symbol.pays[2]),
}))

export const CROOKED_ROAD_DEFINITION = {
  id: 'crooked-road', serverGameId: CROOKED_ROAD_GAME_ID, serverSymbolSetId: CROOKED_ROAD_SYMBOL_SET_ID,
  title: 'The Crooked Road', subtitle: 'Every road has a little mischief',
  description: 'Follow Finbar through a moonlit Irish village, build a crooked road, and step into the Otherworld.',
  initialReels: [['2', '5', '7'], ['3', 'ACE', '4'], ['6', '2', 'POWER'], ['4', '7', '5'], ['2', '6', 'FREE']],
  paylinePatternIds: ruleContract.paylines.map((_, index) => index + 1),
  symbolSpecs: {
    '2': ['Four-leaf clover', '☘', symbols['2']], '3': ['Golden horseshoe', '♧', symbols['3']],
    '4': ['Brass lantern', '🏮', symbols['4']], '5': ['Irish fiddle', '🎻', symbols['5']],
    '6': ['Golden harp', '♫', symbols['6']], '7': ['Clever fox', '🦊', symbols['7']],
    ACE: ['Finbar’s wild hat', '🎩', symbols.ACE], FREE: ['Fairy-door ticket', '🚪', symbols.FREE],
    POWER: ['Enchanted trail', '✦', symbols.POWER],
  },
  symbolGuide: {
    entries: [...payingSymbolGuide,
      { symbol: 'ACE', firstLabel: 'WILD', firstValue: 'substitutes paying symbols', secondLabel: 'All wild', secondValue: 'fox payout' },
      { symbol: 'FREE', firstLabel: `${ruleContract.scatterRequired}+`, firstValue: 'anywhere', secondLabel: 'Award', secondValue: '1 funded journey ticket' },
      { symbol: 'POWER', firstLabel: 'TRAIL', firstValue: 'non-paying symbol', secondLabel: 'Road', secondValue: 'see the road panel' }],
    note: 'Fairy doors award funded journey tickets; the road panel owns destination choices.',
  },
  artwork: { emblem: symbols['2'], accent: symbols.POWER, backdrop: artwork.backgrounds.village,
    title: { src: artwork.wordmark, width: 960, height: 480 } },
  paylineTrace: { color: '#e1bd68', glowColor: '#b8e29b' }, celebrationEffect: 'crooked-road-clover',
  outcomeNarrative: { lossTitle: 'The road stays quiet', winTitle: 'A fortunate find', greatWinTitle: 'The fairies took notice',
    bigWinTitle: 'Otherworld treasure', freeGameSingular: 'journey spin', freeGamePlural: 'journey spins',
    lossNextAction: 'Choose your next spin or tend the road.', winNextAction: 'Your road is ready whenever you are.',
    bonusNextAction: 'The road panel shows your next destination move.' },
}

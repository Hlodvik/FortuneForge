import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { CANDY_CARNIVAL_SOUNDS } from '../../../features/slots/config/soundSets'
import bigTopFreeGame from '../../../assets/slots/games/candy-carnival/optimized/big-top-free-game.png'
import cabinetEmblem from '../../../assets/slots/games/candy-carnival/optimized/candy-carnival-cabinet-emblem-v2.webp'
import blueberryDropCharm from '../../../assets/slots/games/candy-carnival/optimized/blueberry-drop-charm.png'
import candyCrownWild from '../../../assets/slots/games/candy-carnival/optimized/candy-crown-wild.png'
import caramelStarCharm from '../../../assets/slots/games/candy-carnival/optimized/caramel-star-charm.png'
import carnivalCandyBag from '../../../assets/slots/games/candy-carnival/optimized/carnival-candy-bag.png'
import carnivalFair from '../../../assets/slots/games/candy-carnival/optimized/candy-carnival-fair-v2.webp'
import cottonCandyCone from '../../../assets/slots/games/candy-carnival/optimized/cotton-candy-cone.png'
import frostedCupcake from '../../../assets/slots/games/candy-carnival/optimized/frosted-cupcake.png'
import giantJawbreakerPower from '../../../assets/slots/games/candy-carnival/optimized/giant-jawbreaker-power.png'
import goldenCandyCarousel from '../../../assets/slots/games/candy-carnival/optimized/golden-candy-carousel.png'
import limeSwirlCharm from '../../../assets/slots/games/candy-carnival/optimized/lime-swirl-charm.png'
import rainbowCandyToken from '../../../assets/slots/games/candy-carnival/optimized/rainbow-candy-token.png'
import rainbowLollipop from '../../../assets/slots/games/candy-carnival/optimized/rainbow-lollipop.png'
import sprinkleDoughnut from '../../../assets/slots/games/candy-carnival/optimized/sprinkle-doughnut.png'
import strawberryHeartCharm from '../../../assets/slots/games/candy-carnival/optimized/strawberry-heart-charm.png'
import sugarSparkleCharge from '../../../assets/slots/games/candy-carnival/optimized/sugar-sparkle-charge.png'
import tripleCandyCanes from '../../../assets/slots/games/candy-carnival/optimized/triple-candy-canes.png'
import wrappedFruitCandy from '../../../assets/slots/games/candy-carnival/optimized/wrapped-fruit-candy.png'

export const CANDY_CARNIVAL_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'candy-carnival', title: 'Candy Carnival', subtitle: 'The Sweetest Show on Reels',
  description: 'Step under the striped big top for spun sugar, candy rides, and a sparkling prize parade.',
  serverGameId: 'candy-carnival-v1',
  paylinePatternIds: Array.from({ length: 22 }, (_, index) => index + 1),
  presentation: 'juice-glass', collectionAriaLabel: 'Candy jar collections', itemLabel: 'candy charms',
  energyLabel: 'Sugar-rush charge', actorName: 'The carnival candy bag', awardLabel: 'Sweet-shop haul',
  outcomeNarrative: {
    lossTitle: 'No sweet win this spin', winTitle: 'Sweet win', greatWinTitle: 'Sugar rush', bigWinTitle: 'Carnival jackpot',
    freeGameSingular: 'free carnival spin', freeGamePlural: 'free carnival spins',
    lossNextAction: 'Choose a wager, then step under the big top again.',
    winNextAction: 'Choose a wager, then chase another sweet win.',
    bonusNextAction: 'Your next carnival spin is free and uses the locked wager.',
  },
  celebrationEffect: 'candy-sprinkles',
  artwork: { emblem: cabinetEmblem, accent: giantJawbreakerPower, backdrop: carnivalFair },
  valueToken: ['rainbow candy token', '🍬', rainbowCandyToken], motif: '🎪', accentGlyph: '🍭',
  collectionLabels: [
    ['Strawberry heart charm', '💗', strawberryHeartCharm],
    ['Blueberry drop charm', '🔵', blueberryDropCharm],
    ['Caramel star charm', '⭐', caramelStarCharm],
    ['Lime swirl charm', '🟢', limeSwirlCharm],
  ],
  symbolSpecs: {
    '2': ['Wrapped fruit candy', '🍬', wrappedFruitCandy],
    '3': ['Rainbow lollipop', '🍭', rainbowLollipop],
    '4': ['Frosted cupcake', '🧁', frostedCupcake],
    '5': ['Sprinkle doughnut', '🍩', sprinkleDoughnut],
    '6': ['Cotton-candy cone', '🍡', cottonCandyCone],
    '7': ['Golden candy carousel', '🎠', goldenCandyCarousel],
    ACE: ['Candy crown wild', '👑', candyCrownWild],
    FREE: ['Big-top free game', '🎪', bigTopFreeGame],
    POWER: ['Giant jawbreaker power', '🔴', giantJawbreakerPower],
    BOLT: ['Sugar sparkle charge', '✨', sugarSparkleCharge],
    BANANA: ['Triple candy canes', '🍭', tripleCandyCanes],
    PAW: ['Carnival candy bag', '🛍️', carnivalCandyBag],
  },
  specialRound: {
    collectionTarget: 20, collectionAwardedSpins: 8,
    freeGames: { requiredSymbols: 4, awardedSpins: 5 }, sounds: CANDY_CARNIVAL_SOUNDS,
    usesCollections: false, usesEnergy: false, usesDirectValueTokens: false,
    earnHelp: 'Land 4 big-top symbols anywhere in a spin to start five Sugar Parades with a wild reel.',
    feature: {
      id: 'candy-sugar-parade', title: 'Sugar Parade',
      earnLabel: 'Land 4 big tops for 5 Sugar Parades.',
      earnStyle: 'gates', earnHint: 'Big-top lights switch on in a row; all four lights begin a Sugar Parade.',
      activeModes: {
        sync: 'Candy Chain · a matching reel is copied', rows: 'Carousel Stack · two extra rows',
        paw: 'Treat Bag · extra wilds appear', rand: 'Prize Counter · a wild reel appears',
      },
    },
  },
  colors: { skyTop: '#4a1a62', skyBottom: '#e2599a', horizon: '#8f3e89', ground: '#2a103a', primary: '#ff5eaa', secondary: '#60e2e8', deep: '#351047', rim: '#ffe26d', glow: '#ffb8e5', text: '#fff5fb' },
}

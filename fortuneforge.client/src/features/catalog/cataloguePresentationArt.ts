import blackjack from '../../assets/catalogue/non-slots/blackjack-title-v2.webp'
import texasHoldem from '../../assets/catalogue/non-slots/texas-holdem-title-v2.webp'
import threeCardPoker from '../../assets/catalogue/non-slots/three-card-poker-title-v2.webp'
import houseHoldem from '../../assets/catalogue/non-slots/house-holdem-title-v2.webp'
import baccarat from '../../assets/catalogue/non-slots/baccarat-title-v1.webp'
import roulette from '../../assets/catalogue/non-slots/roulette-title-v3.webp'
import craps from '../../assets/catalogue/non-slots/craps-title-v1.webp'
import dragonTiger from '../../assets/catalogue/non-slots/dragon-tiger-title-v1.webp'
import andarBahar from '../../assets/catalogue/non-slots/andar-bahar-title-v1.webp'
import hiLo from '../../assets/catalogue/non-slots/hi-lo-title-v1.webp'
import casinoWar from '../../assets/catalogue/non-slots/casino-war-title-v1.webp'
import videoPoker from '../../assets/catalogue/non-slots/video-poker-title-v1.webp'
import keno from '../../assets/catalogue/non-slots/keno-title-v1.webp'
import sicBo from '../../assets/catalogue/non-slots/sic-bo-title-v1.webp'
import mines from '../../assets/catalogue/non-slots/mines-title-v2.webp'
import solitaire from '../../assets/catalogue/non-slots/solitaire-title-v2.webp'
import hearts from '../../assets/catalogue/non-slots/hearts-title-v2.webp'
import liarsDice from '../../assets/catalogue/non-slots/liars-dice-title-v3.webp'
import twentyFortyEight from '../../assets/catalogue/non-slots/2048-title-v1.webp'
import dropMerge from '../../assets/catalogue/non-slots/drop-merge-title-v1.webp'
import wukong from '../../assets/catalogue/slots/wukong-journey-to-the-west-title-v2.webp'
import rainbowRealm from '../../assets/catalogue/slots/rainbow-realm-title-v2.webp'
import piratesFortune from '../../assets/catalogue/slots/pirates-fortune-title-v2.webp'
import godsOfOlympus from '../../assets/catalogue/slots/gods-of-olympus-title-v2.webp'
import reelRiches from '../../assets/catalogue/slots/reel-riches-title-v2.webp'
import highNoonFortune from '../../assets/catalogue/slots/high-noon-fortune-title-v2.webp'
import royalDraw from '../../assets/catalogue/slots/royal-draw-title-v2.webp'
import arcaneArchives from '../../assets/catalogue/slots/arcane-archives-title-v1.webp'
import cosmicFortune from '../../assets/catalogue/slots/cosmic-fortune-title-v1.webp'
import dinoDominion from '../../assets/catalogue/slots/dino-dominion-title-v1.webp'
import neonNights from '../../assets/catalogue/slots/neon-nights-title-v1.webp'
import jungleJackpot from '../../assets/catalogue/slots/jungle-jackpot-title-v1.webp'
import oceanOdyssey from '../../assets/catalogue/slots/ocean-odyssey-title-v1.webp'
import samuraiFortune from '../../assets/catalogue/slots/samurai-fortune-title-v1.webp'
import candyCarnival from '../../assets/catalogue/slots/candy-carnival-title-v2.webp'
import phantomManor from '../../assets/catalogue/slots/phantom-manor-title-v2.webp'
import nordicLegends from '../../assets/catalogue/slots/nordic-legends-title-v2.webp'
import desertTreasures from '../../assets/catalogue/slots/desert-treasures-title-v2.webp'
import robotRevolution from '../../assets/catalogue/slots/robot-revolution-title-v2.webp'
import dragonHoard from '../../assets/catalogue/slots/dragon-hoard-title-v2.webp'

export type CataloguePresentationArt = Readonly<{
  src: string
  width: 768
  height: 384
}>

const artwork = (src: string): CataloguePresentationArt => Object.freeze({ src, width: 768, height: 384 })

// Presentation data only: importing artwork must never import a playable game or engine.
const artByRoute: ReadonlyMap<string, CataloguePresentationArt> = new Map([
  ['/cards/blackjack', artwork(blackjack)],
  ['/cards/texas-holdem', artwork(texasHoldem)],
  ['/cards/three-card-poker', artwork(threeCardPoker)],
  ['/cards/house-holdem', artwork(houseHoldem)],
  ['/games/baccarat', artwork(baccarat)],
  ['/games/roulette', artwork(roulette)],
  ['/games/craps', artwork(craps)],
  ['/games/dragon-tiger', artwork(dragonTiger)],
  ['/games/andar-bahar', artwork(andarBahar)],
  ['/games/hi-lo', artwork(hiLo)],
  ['/games/casino-war', artwork(casinoWar)],
  ['/games/video-poker', artwork(videoPoker)],
  ['/games/keno', artwork(keno)],
  ['/games/sic-bo', artwork(sicBo)],
  ['/games/mines', artwork(mines)],
  ['/cards/solitaire', artwork(solitaire)],
  ['/cards/hearts', artwork(hearts)],
  ['/games/liars-dice', artwork(liarsDice)],
  ['/games/2048', artwork(twentyFortyEight)],
  ['/games/drop-merge', artwork(dropMerge)],
  ['/slots/wukong', artwork(wukong)],
  ['/slots/rainbow-realm', artwork(rainbowRealm)],
  ['/slots/pirates-fortune', artwork(piratesFortune)],
  ['/slots/gods-of-olympus', artwork(godsOfOlympus)],
  ['/slots/reel-riches', artwork(reelRiches)],
  ['/slots/high-noon-fortune', artwork(highNoonFortune)],
  ['/slots/royal-draw', artwork(royalDraw)],
  ['/slots/arcane-archives', artwork(arcaneArchives)],
  ['/slots/cosmic-fortune', artwork(cosmicFortune)],
  ['/slots/dino-dominion', artwork(dinoDominion)],
  ['/slots/neon-nights', artwork(neonNights)],
  ['/slots/jungle-jackpot', artwork(jungleJackpot)],
  ['/slots/ocean-odyssey', artwork(oceanOdyssey)],
  ['/slots/samurai-fortune', artwork(samuraiFortune)],
  ['/slots/candy-carnival', artwork(candyCarnival)],
  ['/slots/phantom-manor', artwork(phantomManor)],
  ['/slots/nordic-legends', artwork(nordicLegends)],
  ['/slots/desert-treasures', artwork(desertTreasures)],
  ['/slots/robot-revolution', artwork(robotRevolution)],
  ['/slots/dragon-hoard', artwork(dragonHoard)],
])

const cardDemoAliases: ReadonlyMap<string, string> = new Map([
  ['/demo/cards/blackjack', '/cards/blackjack'],
  ['/demo/cards/texas-holdem', '/cards/texas-holdem'],
])

/** Selected title artwork for landing/home cards; unknown routes keep their existing fallback. */
export function getCataloguePresentationArt(route?: string | null): CataloguePresentationArt | undefined {
  if (!route) return undefined
  const pathname = route.split(/[?#]/, 1)[0].replace(/\/+$/, '')
  const canonical = cardDemoAliases.get(pathname)
    ?? (pathname.startsWith('/slots/') ? pathname.replace(/\/demo$/, '') : pathname)
  return artByRoute.get(canonical)
}
